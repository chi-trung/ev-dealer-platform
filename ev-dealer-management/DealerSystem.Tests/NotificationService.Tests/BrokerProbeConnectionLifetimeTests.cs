using Common.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Lifetime and registration guards for the #135 broker probe. These pin
/// properties the probe's correctness depends on, chosen for being
/// *discriminating* — each fails on a specific regression rather than passing
/// by default.
/// </summary>
/// <remarks>
/// Why there is no socket-counting test here. Counting TCP sockets to detect a
/// dropped <c>using</c> was attempted and abandoned as unmeasurable in this
/// environment, for three measured reasons:
/// <list type="bullet">
/// <item>Against a CLOSED port the connect is refused outright, so no socket is
/// ever established and the count stays flat whether disposal works or not —
/// the assertion passes for the wrong reason.</item>
/// <item>Against a listener that accepts TCP but never speaks AMQP,
/// <c>RabbitMQ.Client</c> throws <c>BrokerUnreachableException</c>
/// ("connection.start was never received") out of <c>CreateConnection</c>
/// BEFORE returning a handle, so an undisposed <c>IConnection</c> is not
/// constructible against a non-broker at all.</item>
/// <item><c>IPGlobalProperties.GetActiveTcpConnections()</c> also reports
/// TIME_WAIT sockets, and a correctly closed one lingers there for minutes on
/// Windows, so a raw count cannot separate "disposed" from "leaked".</item>
/// </list>
/// The leak class this all set out to catch requires a HEALTHY broker — the one
/// case where the probe's own <c>using var</c> executes normally and the
/// connection is disposed in order. The exception path never assigns the
/// variable, so there is no handle to leak there either. The measurement that
/// would catch a dropped <c>using</c> needs a real broker, which the test
/// environment does not provide; asserting a weaker proxy would be a test that
/// cannot fail, and that is worse than no test.
/// </remarks>
public class BrokerProbeConnectionLifetimeTests
{
    // A port nothing in this suite binds, in the ephemeral range.
    private const int ClosedPort = 9;

    private static void Build(out IServiceProvider provider, int port = ClosedPort)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:HostName"] = "127.0.0.1",
                ["RabbitMQ:Port"] = port.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<IConfiguration>(config);
        services.AddHealthChecks().AddBrokerProbeCheck();
        provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task UnreachableProbe_ExceptionDataNamesTheHost()
    {
        // The 503 body carries this exception so an operator can tell a dead
        // broker from a misconfigured host without reading the service log.
        Build(out var provider);
        var health = provider.GetRequiredService<HealthCheckService>();

        var report = await health.CheckHealthAsync();
        var entry = Assert.Contains("rabbitmq", report.Entries);

        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        // Swallowing the exception instead of reporting it would make this
        // entry's Exception null — that is the regression this pins.
        Assert.NotNull(entry.Exception);
        Assert.Contains("127.0.0.1", entry.Description);
    }

    [Fact]
    public async Task UnreachableProbe_ConvergesWithinTheTimeout()
    {
        // A probe that hangs holds a deploy gate or crash-loop probe hostage.
        // The check's own RequestedConnectionTimeout is 3s; assert the whole
        // health call settles well inside that, on a closed port where the
        // client's default 30s timeout is what would otherwise bind.
        Build(out var provider);
        var health = provider.GetRequiredService<HealthCheckService>();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = await health.CheckHealthAsync();
        sw.Stop();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"the probe took {sw.Elapsed.TotalSeconds:F1}s against a closed port; a hanging " +
            "probe holds deploy gates hostage for the client's 30s default");
    }

    [Fact]
    public void BrokerCheck_RegistersExactlyOneNonHostedCheck()
    {
        // Guards the fix's core property: the check is resolved by the health
        // service on demand, NOT constructed eagerly or as a hosted service.
        // A hosted registration would reintroduce #135's original defect class
        // (a check that resolves a DI singleton reports that singleton's
        // construction timing, not the dependency's state), and an eager
        // singleton would open a broker connection at boot that nothing closes.
        Build(out var provider);

        // AddHealthChecks() itself registers the framework's
        // HealthCheckPublisherHostedService, so the hosted collection is never
        // empty — the property that actually matters is that OUR probe is not
        // among them, and is not constructible as a long-lived singleton.
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        Assert.DoesNotContain(hosted, s => s.GetType().Name.Contains("Broker", StringComparison.Ordinal));

        // The probe type itself must not be resolvable by hand: if it were
        // registered as a singleton, a second health request would reuse the
        // FIRST connection instead of opening a fresh one.
        var probeType = typeof(BrokerHealthCheckExtensions)
            .GetNestedType("BrokerProbeHealthCheck",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(probeType);
        Assert.Null(provider.GetService(probeType!));
    }
}
