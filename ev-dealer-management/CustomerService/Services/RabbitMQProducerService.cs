using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using CustomerService.Events;

namespace CustomerService.Services
{
    /// <summary>
    /// Real RabbitMQ producer for CustomerService. Customer lifecycle events go to
    /// the "customer_events" topic exchange; test-drive events ride the shared
    /// "vehicle_events" exchange. See docs/EVENTS.md.
    /// </summary>
    public class RabbitMQProducerService : IMessageProducer
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RabbitMQProducerService> _logger;
        private IConnection? _connection;
        private IModel? _channel;

        public RabbitMQProducerService(IConfiguration configuration, ILogger<RabbitMQProducerService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            InitializeRabbitMQ();
        }

        private void InitializeRabbitMQ()
        {
            try
            {
                var factory = new ConnectionFactory()
                {
                    HostName = _configuration["RabbitMQ:HostName"] ?? "localhost",
                    Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                    UserName = _configuration["RabbitMQ:UserName"] ?? "guest",
                    Password = _configuration["RabbitMQ:Password"] ?? "guest"
                };

                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();

                // Both exchanges CustomerService publishes to (idempotent declares).
                _channel.ExchangeDeclare(exchange: EventNames.CustomerExchange, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
                _channel.ExchangeDeclare(exchange: EventNames.VehicleExchange, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);

                _logger.LogInformation("CustomerService RabbitMQ producer initialized.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not connect to RabbitMQ for publishing. Check connection settings.");
            }
        }

        public void PublishMessage<T>(T message, string routingKey = "")
        {
            if (_channel == null || !_channel.IsOpen)
            {
                _logger.LogWarning("RabbitMQ channel is not open. Attempting to re-initialize for publishing.");
                InitializeRabbitMQ();
                if (_channel == null || !_channel.IsOpen)
                {
                    _logger.LogError("Failed to publish message: RabbitMQ channel is still not open.");
                    return;
                }
            }

            try
            {
                var exchange = routingKey.StartsWith("testdrive.", StringComparison.Ordinal)
                    ? EventNames.VehicleExchange
                    : EventNames.CustomerExchange;

                var json = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(json);

                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;

                _channel.BasicPublish(
                    exchange: exchange,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body
                );

                _logger.LogInformation("Published {MessageType} to '{Exchange}'/'{RoutingKey}'",
                    typeof(T).Name, exchange, routingKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing message of type {MessageType} with routing key '{RoutingKey}'",
                    typeof(T).Name, routingKey);
            }
        }

        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
            _logger.LogInformation("CustomerService RabbitMQ producer connection closed.");
        }
    }
}
