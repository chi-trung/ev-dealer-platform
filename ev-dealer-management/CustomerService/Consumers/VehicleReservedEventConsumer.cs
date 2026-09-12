using CustomerService.DTOs;
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
                _channel.QueueDeclare(queue: "customer_vehicle_reserved", durable: true, exclusive: false, autoDelete: false);
                _channel.QueueBind(queue: "customer_vehicle_reserved", exchange: VehicleExchange, routingKey: "vehicle.reserved");

                // Setup consumer
                var consumer = new EventingBasicConsumer(_channel);
                consumer.Received += async (sender, ea) =>
                {
                    try
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);

                        _logger.LogInformation("Received VehicleReservedEvent: {Message}", message);

                        var reservationEvent = JsonSerializer.Deserialize<VehicleReservedEvent>(message);
                        if (reservationEvent == null)
                        {
                            // Unparseable/empty payload: ack and move on. Nack+requeue would
                            // put this message in a poison loop and block the queue.
                            _logger.LogWarning("Received an empty VehicleReservedEvent payload; acking and discarding.");
                            _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                            return;
                        }

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
                        _logger.LogError(ex, "Error processing VehicleReservedEvent");
                        // Reject message and requeue
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                    }
                };

                _channel.BasicConsume(queue: "customer_vehicle_reserved", autoAck: false, consumer: consumer);

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