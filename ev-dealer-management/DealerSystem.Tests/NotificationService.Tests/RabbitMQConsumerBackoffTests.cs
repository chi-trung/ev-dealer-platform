using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Pins the retry schedule in <see cref="RabbitMQConsumerHostedService"/>.
///
/// Regression this exists for: when the broker was absent,
/// <c>RabbitMQConsumerService.StartConsuming</c> reported the failed connect by
/// logging and returning, so <c>await connect</c> completed normally, the
/// <c>catch</c> block never ran, <c>delaySeconds</c> stayed 0, and the loop
/// reconnected in a flat ~4s cadence forever. Measured on 2026-10-03: 79
/// <c>BrokerUnreachableException</c> stack traces logged in 5 minutes while
/// "Could not start RabbitMQ consumers (attempt N)" appeared 0 times - the
/// backoff was computed on every iteration and then thrown away.
///
/// No broker required: the fake below is the only thing that decides whether a
/// connect "succeeds", so these run in CI where no RabbitMQ exists.
/// </summary>
public class RabbitMQConsumerBackoffTests
{
    /// <summary>
    /// Stand-in for the real consumer. <see cref="StartConsuming"/> mirrors the
    /// behaviour that made this a bug: on failure it returns instead of throwing,
    /// exactly as <c>RabbitMQConsumerService</c> does at its "connection is still
    /// not open" branch. The hosted service has to notice that itself.
    /// </summary>
    private sealed class FakeConsumer : IMessageConsumer
    {
        private readonly bool _connectSucceeds;

        public FakeConsumer(bool connectSucceeds) => _connectSucceeds = connectSucceeds;

        public int StartCalls { get; private set; }

        public bool IsConnected => _connectSucceeds;

        public void StartConsuming() => StartCalls++;

        public void StopConsuming() { }
    }

    /// <summary>
    /// Start the hosted service against a broker that never comes up, let it
    /// settle, and report how many connect attempts it managed in that window.
    /// </summary>
    private static async Task<int> AttemptsIn(TimeSpan window)
    {
        var consumer = new FakeConsumer(connectSucceeds: false);
        var service = new RabbitMQConsumerHostedService(
            consumer, NullLogger<RabbitMQConsumerHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        var running = service.StartAsync(cts.Token);

        await Task.Delay(window);
        await service.StopAsync(CancellationToken.None);
        cts.Cancel();
        await running;

        return consumer.StartCalls;
    }

    [Fact]
    public async Task BrokerDown_ReconnectAttempts_AreSpacedOutNotTightLoop()
    {
        // A broker that is down for good must NOT be retried every few seconds.
        // Before the fix this counted ~14 attempts in 15s (flat ~4s each after
        // the poll loop). With a real backoff the first attempts are 2s and 4s,
        // so a 15s window must stay well under that.
        var attempts = await AttemptsIn(TimeSpan.FromSeconds(15));

        Assert.True(attempts < 6,
            $"Expected backoff to space out reconnects, but {attempts} attempts " +
            $"were made in 15s. A count this high means the loop is still spinning " +
            "without applying its delay.");
    }

    [Fact]
    public async Task BrokerDown_DoesNotStopRetrying()
    {
        // The other half: backoff must not turn into "give up". Whatever the
        // schedule, the service has to keep trying, because the broker coming
        // back is the normal case on Render.
        var attempts = await AttemptsIn(TimeSpan.FromSeconds(15));

        Assert.True(attempts >= 2,
            $"Expected repeated retry attempts while the broker is down, got {attempts}. " +
            "A single attempt means the service would never recover.");
    }
}