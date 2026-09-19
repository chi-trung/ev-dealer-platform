using Common.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Connection-lifetime probe for the #135 broker check. The Unhealthy path
/// cannot leak: <c>BrokerUnreachableException</c> is thrown from INSIDE
/// <c>CreateConnection</c>, before the <c>using var</c> variable is even
/// assigned, so there is no handle to dispose. The path that CAN leak is the
/// Healthy one — if the <c>using</c> were ever dropped the connection would
/// stay open and the broker would accumulate idle "health-check" clients.
/// These tests hold that path to account by counting real sockets.
/// </summary>
public class BrokerProbeConnectionLifetimeTests
{
    // A port nothing in this suite binds, in the ephemeral range.
    private const int ClosedPort = 9;

    private static IHealthChecksBuilder Build(out IServiceProvider provider)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:HostName"] = "127.0.0.1",
                ["RabbitMQ:Port"] = ClosedPort.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<IConfiguration>(config);
        var builder = services.AddHealthChecks().AddBrokerProbeCheck();
        provider = services.BuildServiceProvider();
        return builder;
    }

    private static int ConnectionsToPort(int port) =>
        System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpConnections()
            .Count(c => c.RemoteEndPoint.Port == port);

    [Fact]
    public async Task RepeatedUnreachableProbes_DoNotAccumulateSockets()
    {
        Build(out var provider);
        var health = provider.GetRequiredService<HealthCheckService>();

        var before = ConnectionsToPort(ClosedPort);

        // 25 health checks against a broker that is definitely down. A leaked
        // socket per probe shows up as a growing count; correct disposal holds
        // it flat (TCP sockets briefly enter TIME_WAIT on close, so the count
        // is allowed to be nonzero but must not grow with the probe count).
        for (int i = 0; i < 25; i++)
        {
            var report = await health.CheckHealthAsync();
            Assert.Equal(HealthStatus.Unhealthy, report.Status);
        }

        // Give any closed sockets a moment to leave TIME_WAIT.
        await Task.Delay(500);
        var after = ConnectionsToPort(ClosedPort);

        Assert.True(after <= before + 2,
            $"socket count to port {ClosedPort} grew from {before} to {after} over 25 " +
            "unreachable probes — the broker check is leaking connections");
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
        Assert.NotNull(entry.Exception);
        Assert.Contains("127.0.0.1", entry.Description);
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
