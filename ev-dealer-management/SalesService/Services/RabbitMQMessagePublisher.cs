using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SalesService.Services;

/// <summary>
/// RabbitMQ message publisher implementation
/// </summary>
public class RabbitMQMessagePublisher : IMessagePublisher, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMQMessagePublisher> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly object _lock = new object();
    private bool _disposed = false;

    public RabbitMQMessagePublisher(IConfiguration configuration, ILogger<RabbitMQMessagePublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;
        InitializeConnection();
    }

    private void InitializeConnection()
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:HostName"] ?? "localhost",
                Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ:UserName"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest"
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            
            _logger.LogInformation("RabbitMQ connection established successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ connection");
            throw;
        }
    }

    private IModel GetChannel()
    {
        if (_channel == null || !_channel.IsOpen)
        {
            lock (_lock)
            {
                if (_channel == null || !_channel.IsOpen)
                {
                    if (_connection == null || !_connection.IsOpen)
                    {
                        InitializeConnection();
                    }
                    _channel = _connection!.CreateModel();
                }
            }
        }
        return _channel;
    }

    public void PublishMessage<T>(string queueName, T message)
    {
        try
        {
            var channel = GetChannel();
            
            // Declare queue (idempotent operation)
            channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            // Serialize message
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            // Publish message
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true; // Make message persistent

            channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: properties,
                body: body
            );

            _logger.LogInformation("Message published to queue: {QueueName}, Message: {Message}", queueName, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message to queue: {QueueName}", queueName);
            throw;
        }
    }

    public Task PublishMessageAsync<T>(string queueName, T message)
    {
        return Task.Run(() => PublishMessage(queueName, message));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _channel?.Close();
            _channel?.Dispose();
            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
            _logger.LogInformation("RabbitMQ connection disposed");
        }
    }
}

