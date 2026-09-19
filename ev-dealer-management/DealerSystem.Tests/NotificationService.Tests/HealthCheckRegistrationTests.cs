using Common.Health;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #135: every service called <c>AddHealthChecks()</c> with zero registered
/// checks, so <c>/health</c> returned 200 unconditionally — including for a
/// process whose RabbitMQ consumer had permanently died at boot and whose DB
/// was unreachable. These tests pin the fix from the registration side, which
/// is where the defect lived (an empty builder is what makes the endpoint lie):
/// <list type="bullet">
/// <item>the readiness tag is attached, so a caller can separate "process is
/// up" from "this instance can route traffic";</item>
/// <item>the broker and database checks are REGISTERED, not the empty default —
/// an empty <c>AddHealthChecks()</c> would fail both of these assertions;</item>
/// <item>the broker probe reports Unhealthy against an unreachable host
/// instead of throwing or hanging, and the failure names the host;</item>
/// <item>the database check reports Healthy against a real reachable SQLite
/// file, and Unhealthy when the context cannot connect.</item>
/// </list>
/// The probe behaviour is exercised against an unreachable port rather than a
/// real broker for the same reason the other suites use temp SQLite files: the
/// guarantee under test is what the check REPORTS, not whether a broker exists
/// in the test environment. <see cref="BrokerProbeHealthCheck"/> is private, so
/// the check is reached through the public registration surface — the same path
/// every service's Program.cs takes.
/// </summary>
[Collection("sqlite")]
public class HealthCheckRegistrationTests
{
    private static readonly string UnreachableHost = "127.0.0.1";
    // A port nothing in this test suite binds. Chosen in the ephemeral range so
    // a parallel test holding a socket does not turn this into a false pass;
    // the assertion below is that the check reports Unhealthy, and a genuinely
    // listening port would make it Healthy and fail the test — so the probe
    // must target something reliably closed.
    private static readonly int ClosedPort = 9;

    private static (IServiceProvider services, HealthCheckService health) Build(
        Action<IHealthChecksBuilder>? configure = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:HostName"] = UnreachableHost,
                ["RabbitMQ:Port"] = ClosedPort.ToString(),
                ["RabbitMQ:UserName"] = "guest",
                ["RabbitMQ:Password"] = "guest"
            })
            .Build();

        var services = new ServiceCollection();
        // DefaultHealthCheckService takes ILogger<T> as a constructor
        // dependency and reads HealthCheckServiceOptions from the named-options
        // system, so a bare ServiceCollection can activate neither. AddHealthChecks
        // does not pull either in — the full host's wiring does. This test asserts
        // what the checks REPORT, so a no-op logging provider is enough; real
        // services get the real logger and options from their host.
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddOptions();
        services.AddSingleton<IConfiguration>(config);
        var builder = services.AddHealthChecks();
        configure?.Invoke(builder);
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<HealthCheckService>());
    }

    [Fact]
    public void AddBrokerProbeCheck_RegistersCheckUnderTheReadinessTag()
    {
        var (services, _) = Build(b => b.AddBrokerProbeCheck());

        // HealthCheckServiceOptions is a NAMED options object, so it is read
        // through IOptionsMonitor<T> with the default name rather than
        // resolved directly — resolving the type itself throws even with
        // AddOptions registered, because the options system only materializes
        // named options on demand.
        var options = services.GetRequiredService<IOptionsMonitor<HealthCheckServiceOptions>>()
            .Get(Options.DefaultName);
        // The registration is what #135 was missing: an empty AddHealthChecks()
        // leaves this collection empty and /health answering 200 unconditionally.
        var broker = Assert.Single(options.Registrations, r => r.Name == "rabbitmq");
        Assert.Contains(BrokerHealthCheckExtensions.ReadinessTag, broker.Tags);
    }

    [Fact]
    public async Task BrokerProbe_ReportsUnhealthyWhenTheBrokerIsUnreachable()
    {
        var (_, health) = Build(b => b.AddBrokerProbeCheck());

        var report = await health.CheckHealthAsync();

        // The probe must converge on Unhealthy, not throw out of the check and
        // not hang: before #135 a dead broker at boot was invisible because no
        // check existed to report it, and a check that throws instead of
        // reporting would surface as a 500, not the 503 a deploy gate needs.
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        var entry = Assert.Contains("rabbitmq", report.Entries);
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains(UnreachableHost, entry.Description);
    }

    [Fact]
    public async Task BrokerProbe_CheckNameIsConfigurable()
    {
        // render.yaml and the gateway aggregate key off this name; a service
        // registering a different one would silently stop appearing in the
        // gateway's per-service breakdown.
        var (_, health) = Build(b => b.AddBrokerProbeCheck("message-broker"));

        var report = await health.CheckHealthAsync();
        Assert.Contains("message-broker", report.Entries);
        Assert.DoesNotContain("rabbitmq", report.Entries);
    }

    [Fact]
    public async Task DatabaseCheck_ReportsHealthyWhenTheDatabaseIsReachable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"health_db_{Guid.NewGuid():N}.db");
        await using (var db = new NotificationDbContext(
            new DbContextOptionsBuilder<NotificationDbContext>()
                .UseSqlite($"Data Source={dbPath}").Options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // The check resolves the context from the SAME container it is registered
        // in: building a second root container (a first attempt at this helper)
        // would mint a fresh singleton and report state the app never uses.
        services.AddDbContext<NotificationDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddHealthChecks()
            .AddDatabaseCheck<NotificationDbContext>();
        var provider = services.BuildServiceProvider();

        try
        {
            var report = await provider.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync();
            var entry = Assert.Contains("database", report.Entries);
            Assert.Equal(HealthStatus.Healthy, entry.Status);
        }
        finally
        {
            // The check's scoped DbContext returns to the pool on disposal, but
            // Microsoft.Data.Sqlite keeps the file HANDLE open for reuse, so
            // DeleteFile throws "being used by another process" on Windows.
            // Clearing the pool for this exact connection string is what closes
            // it; the other suites' temp DBs hit the same pooling behaviour.
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={dbPath}"));
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task DatabaseCheck_ReportsUnhealthyWhenTheContextCannotConnect()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // A directory that does not exist: SQLite cannot create the file there,
        // so CanConnect is false rather than the provider constructing a schema
        // on demand. This is the post-boot failure the check exists to catch —
        // a DB that was reachable at startup and went away while the instance
        // was serving traffic.
        services.AddDbContext<NotificationDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "no_such_dir_135", "x.db")}"));
        services.AddHealthChecks()
            .AddDatabaseCheck<NotificationDbContext>();
        var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync();
        var entry = Assert.Contains("database", report.Entries);
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
    }
}
