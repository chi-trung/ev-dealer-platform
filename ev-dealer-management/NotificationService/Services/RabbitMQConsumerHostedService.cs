using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationService.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NotificationService.Services
{
    /// <summary>
    /// Drives <see cref="IMessageConsumer.StartConsuming"/> for the life of the
    /// process and keeps retrying while the broker is unreachable.
    /// </summary>
    /// <remarks>
    /// This used to be an <see cref="IHostedService"/> whose <c>StartAsync</c>
    /// called <c>StartConsuming()</c> exactly once and returned
    /// <c>Task.CompletedTask</c>. <c>StartConsuming</c> itself only retried the
    /// connection once and then <c>return</c>ed on failure, so a broker that was
    /// not listening yet at boot left all 14 queues dead for the rest of the
    /// process lifetime while <c>/health</c> kept answering 200. Docker compose
    /// hides this with <c>depends_on: service_healthy</c>; Render starts the seven
    /// web services in parallel against a broker that may still be starting, so
    /// on Render this was the normal case, not the edge case.
    /// </remarks>
    public class RabbitMQConsumerHostedService : BackgroundService
    {
        // Backoff schedule in seconds. 2^attempt capped at 30: fast enough that a
        // broker coming back is picked up within ~30s, slow enough that a broker
        // that is down for good does not fill the log with connection errors.
        private const int MaxBackoffSeconds = 30;

        // How long ExecuteAsync waits for one connect attempt before giving up
        // on it and looping. Must exceed RabbitMQ:ConnectionTimeoutSeconds
        // (default 10s) so the normal failure path is the consumer's own
        // timeout, not this one; the gap is slack, not a second mechanism.
        private const int ConnectionGraceSeconds = 15;

        private readonly IMessageConsumer _messageConsumer;
        private readonly ILogger<RabbitMQConsumerHostedService> _logger;

        public RabbitMQConsumerHostedService(IMessageConsumer messageConsumer, ILogger<RabbitMQConsumerHostedService> logger)
        {
            _messageConsumer = messageConsumer;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RabbitMQ Consumer Hosted Service running.");

            var attempt = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                double delaySeconds = 0;
                try
                {
                    // StartConsuming -> InitializeRabbitMQ -> CreateConnection()
                    // is synchronous in RabbitMQ.Client 6.8.1. Called directly
                    // here it would block the thread the host awaits in
                    // StartAsync, which is why the port never bound when the
                    // broker was absent: the host had not reached app.Run() yet.
                    // Run it off-thread and stop waiting after ConnectionGrace so
                    // a hung connect cannot hold startup open; the orphaned task
                    // is abandoned, not awaited, and the backoff loop below is
                    // what actually re-establishes the connection.
                    var connect = Task.Run(_messageConsumer.StartConsuming, stoppingToken);
                    var grace = TimeSpan.FromSeconds(ConnectionGraceSeconds);
                    if (await Task.WhenAny(connect, Task.Delay(grace, stoppingToken)) != connect)
                    {
                        _logger.LogWarning(
                            "RabbitMQ connect did not complete within {Grace}s; retrying without waiting for it.",
                            ConnectionGraceSeconds);
                        attempt++;
                        delaySeconds = Math.Min(MaxBackoffSeconds, Math.Pow(2, Math.Min(attempt, 5)));
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                        continue;
                    }

                    // Propagate a failure from the connect task into the catch
                    // below rather than swallowing it, so backoff still applies.
                    await connect;

                    // StartConsuming reports a failed connect by logging and
                    // returning, so `await connect` completing does NOT mean
                    // the broker is reachable. Without this check the catch
                    // below never ran, delaySeconds stayed 0, and the loop
                    // reconnected every few seconds forever with the backoff
                    // schedule computed but never used - measured at 79 stack
                    // traces in 5 minutes with zero attempts logged.
                    if (!_messageConsumer.IsConnected)
                    {
                        throw new InvalidOperationException(
                            "RabbitMQ connection is not open after StartConsuming.");
                    }

                    // StartConsuming is fire-and-forget (EventingBasicConsumer
                    // dispatches on its own threads), so a successful call does
                    // not mean the queues stay healthy - a broker that dies
                    // later is detected by the per-queue handlers, not here.
                    // Poll IsConnected so a connection lost mid-life is re-opened
                    // instead of silently stranding every consumer.
                    while (!stoppingToken.IsCancellationRequested && _messageConsumer.IsConnected)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }

                    if (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }

                    _logger.LogWarning("RabbitMQ connection dropped; re-initializing consumers.");
                    // Resetting here is safe only because of the IsConnected
                    // check above: this line is now reachable ONLY after a
                    // connection was actually established and then lost, never
                    // after a failed connect. That is what lets the reset be a
                    // genuine fresh start while failures keep counting up
                    // through the catch below.
                    attempt = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    attempt++;
                    delaySeconds = Math.Min(MaxBackoffSeconds, Math.Pow(2, Math.Min(attempt, 5)));
                    _logger.LogError(ex,
                        "Could not start RabbitMQ consumers (attempt {Attempt}); retrying in {Delay}s",
                        attempt, delaySeconds);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RabbitMQ Consumer Hosted Service is stopping.");
            _messageConsumer.StopConsuming();
            await base.StopAsync(cancellationToken);
        }
    }
}
