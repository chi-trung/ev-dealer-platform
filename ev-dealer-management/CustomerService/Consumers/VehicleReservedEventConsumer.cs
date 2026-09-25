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
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
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
                var retryTtlMs = int.Parse(_configuration["RabbitMQ:RetryTtlMilliseconds"] ?? "5000");
                var maxAttempts = int.Parse(_configuration["RabbitMQ:MaxDeliveryAttempts"] ?? "3");
                EventRetryPolicy.DeclareRetryTopology(_connection, Queue, retryTtlMs);

                // Prefetch 1: process one delivery at a time. The handler does a
                // check-then-insert on Customers.Email (unique index); concurrent
                // deliveries of the same email would race and lose.
                _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

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
                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        // Transient handler failure (DB down, race): re-publish to the
                        // .retry queue (TTL delay) instead of the old nack+requeue
                        // hot-loop. After RabbitMQ:MaxDeliveryAttempts rounds the
                        // message is parked in the DLQ.
                        var rounds = EventRetryPolicy.RetryRounds(ea, Queue);
                        if (rounds >= maxAttempts)
                        {
                            _logger.LogError(ex, "VehicleReservedEvent exhausted {Max} retries; parking in DLQ. Body: {Message}", maxAttempts, message);
                            Park(ea);
                            return;
                        }

                        _logger.LogWarning(ex, "Error processing VehicleReservedEvent (attempt {Attempt}/{Max}); retrying in {Ttl}ms",
                            rounds + 1, maxAttempts, retryTtlMs);
                        try
                        {
                            EventRetryPolicy.ScheduleRetry(_connection!, _channel, ea, Queue, retryTtlMs);
                        }
                        catch (Exception retryEx)
                        {
                            _logger.LogError(retryEx, "Could not schedule retry; falling back to requeue.");
                            try { _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true); }
                            catch (Exception nackEx) { _logger.LogError(nackEx, "Fallback nack failed."); }
                        }
                    }
                };

                _channel.BasicConsume(queue: Queue, autoAck: false, consumer: consumer);

                _logger.LogInformation("VehicleReservedEventConsumer started and waiting for messages...");

                // Keep running until cancelled
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in VehicleReservedEventConsumer");
            }
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