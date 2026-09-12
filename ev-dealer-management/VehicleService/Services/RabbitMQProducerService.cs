using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using VehicleService.Events;

namespace VehicleService.Services
{
    public class RabbitMQProducerService : IMessageProducer
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RabbitMQProducerService> _logger;
        private IConnection? _connection;
        private IModel? _channel;
        // IModel is not thread-safe: serialize publishes (and reconnects) from
        // concurrent HTTP requests onto the single channel.
        private readonly object _publishLock = new object();

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
                    HostName = _configuration["RabbitMQ:HostName"],
                    Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                    UserName = _configuration["RabbitMQ:UserName"],
                    Password = _configuration["RabbitMQ:Password"]
                };

                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();

                // Declare the shared topic exchange (idempotent). All vehicle.* events
                // go through it so multiple consumers can fan out from one publish.
                _channel.ExchangeDeclare(
                    exchange: EventNames.VehicleExchange,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    arguments: null
                );

                _logger.LogInformation("RabbitMQ producer connection and channel initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not connect to RabbitMQ for publishing. Check connection settings.");
            }
        }

        public void PublishMessage<T>(T message, string routingKey = "")
        {
            lock (_publishLock)
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
                    var messageString = JsonSerializer.Serialize(message);
                    var body = Encoding.UTF8.GetBytes(messageString);

                    // Determine routing key based on message type when not given explicitly.
                    var key = routingKey;
                    if (string.IsNullOrEmpty(key))
                    {
                        key = typeof(T).Name switch
                        {
                            "VehicleCreatedEvent" => EventNames.VehicleCreated,
                            "VehicleUpdatedEvent" => EventNames.VehicleUpdated,
                            "VehicleDeletedEvent" => EventNames.VehicleDeleted,
                            "VehicleReservedEvent" => EventNames.VehicleReserved,
                            _ => "vehicle.events"
                        };
                    }

                    // Persistent so reserved/created events survive a broker restart.
                    var properties = _channel.CreateBasicProperties();
                    properties.Persistent = true;

                    _channel.BasicPublish(
                        exchange: EventNames.VehicleExchange,
                        routingKey: key,
                        basicProperties: properties,
                        body: body
                    );

                    _logger.LogInformation("Published message of type {MessageType} to exchange '{Exchange}' with routing key '{RoutingKey}'",
                        typeof(T).Name, EventNames.VehicleExchange, key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error publishing message of type {MessageType}", typeof(T).Name);
                }
            }
        }

        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
            _logger.LogInformation("RabbitMQ producer connection closed.");
        }
    }
}
