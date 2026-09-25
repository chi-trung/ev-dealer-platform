using Common.Events;
using CustomerService.DTOs;
using CustomerService.Events;
using CustomerService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace CustomerService.Consumers
{
    public class VehicleReservedEventConsumer : BackgroundService
    {
        // Same topic exchange VehicleService publishes vehicle.reserved to (docs/EVENTS.md).
        private const string VehicleExchange = "vehicle_events";
        private const string Queue = "customer_vehicle_reserved";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VehicleReservedEventConsumer> _logger;
        private readonly IConfiguration _configuration;
        // Read once here rather than in Connect() because the Received handler
        // below is a lambda that outlives the method that set them up.
        private readonly int _retryTtlMs;
        private readonly int _maxAttempts;
        private IConnection? _connection;
        private IModel? _channel;

        public VehicleReservedEventConsumer(
            IServiceScopeFactory scopeFactory,
            ILogger<VehicleReservedEventConsumer> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
            _retryTtlMs = int.Parse(configuration["RabbitMQ:RetryTtlMilliseconds"]
                ?? EventRetryPolicy.DefaultRetryTtlMs.ToString());
            _maxAttempts = int.Parse(configuration["RabbitMQ:MaxDeliveryAttempts"]
                ?? EventRetryPolicy.DefaultMaxAttempts.ToString());
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Connect-retry loop. The old shape was one try around the whole
            // method whose catch only logged and returned: a broker that was
            // down at boot made this background service finish successfully
            // while /health kept answering 200, so the consumer was dead for
            // the life of the process and nothing said so. Docker compose
            // hides this with depends_on: service_healthy, but Render has no
            // equivalent - six web services start in parallel against a
            // broker that may not be listening yet.
            var attempt = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Connect();
                    attempt = 0;
                    await ConsumeAsync(stoppingToken);
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    attempt++;
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5))));
                    _logger.LogError(ex,
                        "RabbitMQ unavailable for {Queue} (attempt {Attempt}); retrying in {Delay}s",
                        Queue, attempt, delay.TotalSeconds);
                    SafeClose();
                    try { await Task.Delay(delay, stoppingToken); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        private void Connect()
        {
            // Setup RabbitMQ connection from configuration, like every other service.
            var factory = new ConnectionFactory()
            {
                HostName = _configuration["RabbitMQ:HostName"] ?? "localhost",
                Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ:UserName"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest"
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare exchange and queue, then bind this service's own queue
            // to the shared topic exchange.
            _channel.ExchangeDeclare(exchange: VehicleExchange, type: ExchangeType.Topic, durable: true);
            _channel.QueueDeclare(queue: Queue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(queue: Queue, exchange: VehicleExchange, routingKey: "vehicle.reserved");
            EventRetryPolicy.DeclareRetryTopology(_connection, Queue, _retryTtlMs);
        }

        /// <summary>
        /// Subscribes and then stays alive until cancellation. Any throw here
        /// (broker dropped mid-flight, channel faulted) propagates to
        /// <see cref="ExecuteAsync"/>, which reconnects instead of ending the
        /// background service.
        /// </summary>
        private async Task ConsumeAsync(CancellationToken stoppingToken)
        {
            // Prefetch 1: process one delivery at a time. The handler does a
            // check-then-insert on Customers.Email (unique index); concurrent
            // deliveries of the same email would race and lose.
            _channel!.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            // Setup consumer
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                VehicleReservedEvent? reservationEvent;
                try
                {
                    reservationEvent = JsonSerializer.Deserialize<VehicleReservedEvent>(message);
                }
                catch (JsonException ex)
                {
                    // Unparseable payload (empty body, truncated, foreign message):
                    // retrying never helps, but discarding loses it silently - park
                    // it in customer_vehicle_reserved.dlq for operators instead
                    // (docs/EVENTS.md, W4).
                    _logger.LogWarning(ex, "Received a malformed VehicleReservedEvent payload; parking in DLQ. Body: {Message}", message);
                    Park(ea);
                    return;
                }

                if (reservationEvent == null)
                {
                    _logger.LogWarning("Received a null VehicleReservedEvent payload; parking in DLQ.");
                    Park(ea);
                    return;
                }

                try
                {
                    _logger.LogInformation("Received VehicleReservedEvent: {Message}", message);

                    using var scope = _scopeFactory.CreateScope();
                    var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();

                    var customer = await customerService.CreateOrUpdateCustomerFromReservationAsync(reservationEvent);
                    _logger.LogInformation("Customer created/updated: {CustomerName} (ID: {CustomerId})",
                        customer.Name, customer.Id);

                    // Acknowledge message
                    _channel!.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    // Transient handler failure (DB down, race): re-publish to the
                    // .retry queue (TTL delay) instead of the old nack+requeue
                    // hot-loop. After RabbitMQ:MaxDeliveryAttempts rounds the
                    // message is parked in the DLQ.
                    var rounds = EventRetryPolicy.RetryRounds(ea, Queue);
                    if (rounds >= _maxAttempts)
                    {
                        _logger.LogError(ex, "VehicleReservedEvent exhausted {Max} retries; parking in DLQ. Body: {Message}", _maxAttempts, message);
                        Park(ea);
                        return;
                    }

                    _logger.LogWarning(ex, "Error processing VehicleReservedEvent (attempt {Attempt}/{Max}); retrying in {Ttl}ms",
                        rounds + 1, _maxAttempts, _retryTtlMs);
                    try
                    {
                        EventRetryPolicy.ScheduleRetry(_connection!, _channel!, ea, Queue, _retryTtlMs);
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogError(retryEx, "Could not schedule retry; falling back to requeue.");
                        try { _channel!.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true); }
                        catch (Exception nackEx) { _logger.LogError(nackEx, "Fallback nack failed."); }
                    }
                }
            };

            _channel.BasicConsume(queue: Queue, autoAck: false, consumer: consumer);

            _logger.LogInformation("VehicleReservedEventConsumer started and waiting for messages...");

            // Keep running until cancelled. RabbitMQ.Client 6.x surfaces a
            // broker restart as an AlreadyClosedException on the next I/O,
            // so this delay alone is not enough to notice - but BasicConsume
            // above already threw if the channel died before subscribing, and
            // the handler's failures are handled in place. Cancelling exits
            // through the OperationCanceledException filter in ExecuteAsync.
            //
            // The IsOpen checks are load-bearing, not defensive noise. With
            // RabbitMQ.Client 6.x a broker that goes away does NOT throw out of
            // this loop: the channel is closed by the client's shutdown
            // handling, the eventing consumer simply stops dispatching, and
            // Task.Delay keeps ticking happily. Measured on a live broker stop:
            // the consumer went silent with zero log output and /health kept
            // answering 200. Polling IsOpen is what turns a silent death into
            // a thrown AlreadyClosedException that ExecuteAsync can retry on.
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_connection is not { IsOpen: true } || _channel is not { IsOpen: true })
                {
                    // Thrown on purpose rather than returning: returning would
                    // end ExecuteAsync and kill the consumer for good, which is
                    // the exact failure this loop exists to prevent.
                    throw new InvalidOperationException(
                        "Consumer connection or channel is no longer open; re-establishing.");
                }
                await Task.Delay(1000, stoppingToken);
            }
        }

        /// <summary>
        /// Best-effort teardown of a connection that may be half-built or
        /// already dead. Called on the reconnect path, where a throw here would
        /// mask the real connection error, so every step is swallowed.
        /// </summary>
        private void SafeClose()
        {
            try { _channel?.Close(); } catch { /* already closed or never opened */ }
            try { _channel?.Dispose(); } catch { }
            _channel = null;
            try { _connection?.Close(); } catch { }
            try { _connection?.Dispose(); } catch { }
            _connection = null;
        }

        /// <summary>
        /// Parks a delivery in the dead-letter queue (body retained), falling
        /// back to nack+requeue if the publish fails (dead channel).
        /// </summary>
        private void Park(BasicDeliverEventArgs ea)
        {
            try
            {
                EventRetryPolicy.ParkInDeadLetterQueue(_channel!, ea, Queue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not park message in DLQ; requeueing instead.");
                try { _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true); }
                catch (Exception nackEx) { _logger.LogError(nackEx, "Fallback nack failed."); }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping VehicleReservedEventConsumer...");
            
            if (_channel != null)
            {
                _channel.Close();
                _channel.Dispose();
            }

            if (_connection != null)
            {
                _connection.Close();
                _connection.Dispose();
            }

            await base.StopAsync(cancellationToken);
        }
    }
}