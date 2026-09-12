using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using NotificationService.DTOs;
using NotificationService.Consumers;
using Serilog;

namespace NotificationService.Services
{
    public class RabbitMQConsumerService : IMessageConsumer, IDisposable
    {
        // Topic exchange published by VehicleService (vehicle.*) and
        // CustomerService (testdrive.*). See docs/EVENTS.md.
        private const string VehicleExchange = "vehicle_events";

        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private IConnection? _connection;
        private IModel? _saleChannel;
        private IModel? _reservationChannel;
        private IModel? _testDriveChannel;

        public RabbitMQConsumerService(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
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

                // Channel for SaleCompleted events (default exchange, published by SalesService)
                _saleChannel = _connection.CreateModel();
                var saleQueue = _configuration["RabbitMQ:Queues:SaleCompleted"] ?? "sales.completed";
                _saleChannel.QueueDeclare(queue: saleQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);

                // Channel for VehicleReserved events - must bind to the vehicle_events topic
                // exchange, otherwise the queue receives nothing.
                _reservationChannel = _connection.CreateModel();
                var reservationQueue = _configuration["RabbitMQ:Queues:VehicleReserved"] ?? "vehicle.reserved";
                _reservationChannel.ExchangeDeclare(exchange: VehicleExchange, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
                _reservationChannel.QueueDeclare(queue: reservationQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
                _reservationChannel.QueueBind(queue: reservationQueue, exchange: VehicleExchange, routingKey: "vehicle.reserved");

                // Channel for TestDriveScheduled events - also on the shared topic exchange.
                _testDriveChannel = _connection.CreateModel();
                var testDriveQueue = _configuration["RabbitMQ:Queues:TestDriveScheduled"] ?? "testdrive.scheduled";
                _testDriveChannel.ExchangeDeclare(exchange: VehicleExchange, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
                _testDriveChannel.QueueDeclare(queue: testDriveQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
                _testDriveChannel.QueueBind(queue: testDriveQueue, exchange: VehicleExchange, routingKey: "testdrive.scheduled");

                Log.Information("RabbitMQ consumer connection and channels initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not connect to RabbitMQ for consuming. Check connection settings.");
            }
        }

        public void StartConsuming()
        {
            if (_connection == null || !_connection.IsOpen)
            {
                Log.Warning("RabbitMQ connection is not open. Attempting to re-initialize.");
                InitializeRabbitMQ();
                if (_connection == null || !_connection.IsOpen)
                {
                    Log.Error("Failed to start consuming: RabbitMQ connection is still not open.");
                    return;
                }
            }

            // Start consuming SaleCompleted events
            StartConsumingSaleCompleted();

            // Start consuming VehicleReserved events
            StartConsumingVehicleReserved();

            // Start consuming TestDriveScheduled events
            StartConsumingTestDriveScheduled();

            Log.Information("Started consuming messages from all queues.");
        }

        private void StartConsumingSaleCompleted()
        {
            if (_saleChannel == null || !_saleChannel.IsOpen) return;

            var saleQueue = _configuration["RabbitMQ:Queues:SaleCompleted"] ?? "sales.completed";
            var consumer = new EventingBasicConsumer(_saleChannel);

            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var saleConsumer = scope.ServiceProvider.GetRequiredService<SaleCompletedConsumer>();
                    await saleConsumer.HandleAsync(message);
                    _saleChannel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error processing SaleCompleted message: {Message}", message);
                    _saleChannel.BasicNack(ea.DeliveryTag, false, true);
                }
            };

            _saleChannel.BasicConsume(queue: saleQueue, autoAck: false, consumer: consumer);
            Log.Information("Started consuming from queue: {QueueName}", saleQueue);
        }

        private void StartConsumingVehicleReserved()
        {
            if (_reservationChannel == null || !_reservationChannel.IsOpen) return;

            var reservationQueue = _configuration["RabbitMQ:Queues:VehicleReserved"] ?? "vehicle.reserved";
            var consumer = new EventingBasicConsumer(_reservationChannel);

            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var reservationConsumer = scope.ServiceProvider.GetRequiredService<VehicleReservedConsumer>();
                    await reservationConsumer.HandleAsync(message);
                    _reservationChannel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error processing VehicleReserved message: {Message}", message);
                    _reservationChannel.BasicNack(ea.DeliveryTag, false, true);
                }
            };

            _reservationChannel.BasicConsume(queue: reservationQueue, autoAck: false, consumer: consumer);
            Log.Information("Started consuming from queue: {QueueName}", reservationQueue);
        }

        private void StartConsumingTestDriveScheduled()
        {
            if (_testDriveChannel == null || !_testDriveChannel.IsOpen) return;

            var testDriveQueue = _configuration["RabbitMQ:Queues:TestDriveScheduled"] ?? "testdrive.scheduled";
            var consumer = new EventingBasicConsumer(_testDriveChannel);

            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var testDriveConsumer = scope.ServiceProvider.GetRequiredService<TestDriveScheduledConsumer>();
                    await testDriveConsumer.HandleAsync(message);
                    _testDriveChannel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error processing TestDriveScheduled message: {Message}", message);
                    _testDriveChannel.BasicNack(ea.DeliveryTag, false, true);
                }
            };

            _testDriveChannel.BasicConsume(queue: testDriveQueue, autoAck: false, consumer: consumer);
            Log.Information("Started consuming from queue: {QueueName}", testDriveQueue);
        }

        public void StopConsuming()
        {
            Log.Information("Stopping RabbitMQ consumer.");
            Dispose();
        }

        public void Dispose()
        {
            _saleChannel?.Close();
            _reservationChannel?.Close();
            _testDriveChannel?.Close();
            _connection?.Close();
            Log.Information("RabbitMQ consumer connection closed.");
        }
    }
}
