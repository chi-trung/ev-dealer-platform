using RabbitMQ.Client;

using Common.Events;
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

        // Topic exchange published by CustomerService (customer.*).
        private const string CustomerExchange = "customer_events";

        // One channel per consumed queue: IModel is not thread-safe and a
        // delivery must be acked/rejected on its own channel.
        private const string SaleQueueKey = "SaleCompleted";
        private const string ReservationQueueKey = "VehicleReserved";
        private const string TestDriveQueueKey = "TestDriveScheduled";
        private const string OrderQueueKey = "OrderCreated";
        private const string QuoteQueueKey = "QuoteCreated";
        private const string ContractQueueKey = "ContractCreated";
        private const string CustomerCreatedQueueKey = "CustomerCreated";
        private const string CustomerUpdatedQueueKey = "CustomerUpdated";
        private const string CustomerDeletedQueueKey = "CustomerDeleted";
        private const string PaymentReceivedQueueKey = "PaymentReceived";
        private const string OrderStatusChangedQueueKey = "OrderStatusChanged";
        private const string VehicleCreatedQueueKey = "VehicleCreated";
        private const string VehicleUpdatedQueueKey = "VehicleUpdated";
        private const string VehicleDeletedQueueKey = "VehicleDeleted";

        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly object _initLock = new object();
        private IConnection? _connection;
        private IModel? _saleChannel;
        private IModel? _reservationChannel;
        private IModel? _testDriveChannel;
        private IModel? _orderChannel;
        private IModel? _quoteChannel;
        private IModel? _contractChannel;
        private IModel? _customerCreatedChannel;
        private IModel? _customerUpdatedChannel;
        private IModel? _customerDeletedChannel;
        private IModel? _paymentReceivedChannel;
        private IModel? _orderStatusChangedChannel;
        private IModel? _vehicleCreatedChannel;
        private IModel? _vehicleUpdatedChannel;
        private IModel? _vehicleDeletedChannel;
        private int _maxAttempts = EventRetryPolicy.DefaultMaxAttempts;
        private int _retryTtlMs = EventRetryPolicy.DefaultRetryTtlMs;

        public RabbitMQConsumerService(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            InitializeRabbitMQ();
        }

        private string QueueName(string key, string fallback) =>
            _configuration[$"RabbitMQ:Queues:{key}"] ?? fallback;

        private void InitializeRabbitMQ()
        {
            lock (_initLock)
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
                    _maxAttempts = int.Parse(_configuration["RabbitMQ:MaxDeliveryAttempts"] ?? "3");
                    _retryTtlMs = int.Parse(_configuration["RabbitMQ:RetryTtlMilliseconds"] ?? "5000");

                    // SaleCompleted events (default exchange, published by SalesService)
                    _saleChannel = OpenQueueChannel(QueueName(SaleQueueKey, "sales.completed"), vehicleTopicRoutingKey: null);

                    // VehicleReserved events - must bind to the vehicle_events topic
                    // exchange, otherwise the queue receives nothing. The routing key
                    // is the EVENT name, not the (configurable) queue name.
                    _reservationChannel = OpenQueueChannel(QueueName(ReservationQueueKey, "vehicle.reserved"), "vehicle.reserved");

                    // TestDriveScheduled events - also on the shared topic exchange.
                    _testDriveChannel = OpenQueueChannel(QueueName(TestDriveQueueKey, "testdrive.scheduled"), "testdrive.scheduled");

                    // Sales lifecycle events published by SalesService to the default
                    // exchange (routing key = queue name): order.created, quote.created,
                    // contract.created.
                    _orderChannel = OpenQueueChannel(QueueName(OrderQueueKey, "order.created"), vehicleTopicRoutingKey: null);
                    _quoteChannel = OpenQueueChannel(QueueName(QuoteQueueKey, "quote.created"), vehicleTopicRoutingKey: null);
                    _contractChannel = OpenQueueChannel(QueueName(ContractQueueKey, "contract.created"), vehicleTopicRoutingKey: null);

                    // Customer lifecycle events published by CustomerService to
                    // the customer_events topic exchange. Like the vehicle
                    // queues above, these bind by EVENT routing key, never the
                    // queue name (renames via RabbitMQ:Queues must not unbind).
                    _customerCreatedChannel = OpenTopicQueueChannel(QueueName(CustomerCreatedQueueKey, "customer.created"), CustomerExchange, "customer.created");
                    _customerUpdatedChannel = OpenTopicQueueChannel(QueueName(CustomerUpdatedQueueKey, "customer.updated"), CustomerExchange, "customer.updated");
                    _customerDeletedChannel = OpenTopicQueueChannel(QueueName(CustomerDeletedQueueKey, "customer.deleted"), CustomerExchange, "customer.deleted");

                    // Sales-side lifecycle events published by SalesService to
                    // the default exchange (routing key = queue name, from
                    // RabbitMQ:Queues:PaymentReceived/OrderStatusChanged):
                    // payment.received, order.status.changed.
                    _paymentReceivedChannel = OpenQueueChannel(QueueName(PaymentReceivedQueueKey, "payment.received"), vehicleTopicRoutingKey: null);
                    _orderStatusChangedChannel = OpenQueueChannel(QueueName(OrderStatusChangedQueueKey, "order.status.changed"), vehicleTopicRoutingKey: null);

                    // Vehicle lifecycle events published by VehicleService to
                    // the vehicle_events topic exchange - same bind-by-event-key
                    // contract as the customer queues above.
                    _vehicleCreatedChannel = OpenTopicQueueChannel(QueueName(VehicleCreatedQueueKey, "vehicle.created"), VehicleExchange, "vehicle.created");
                    _vehicleUpdatedChannel = OpenTopicQueueChannel(QueueName(VehicleUpdatedQueueKey, "vehicle.updated"), VehicleExchange, "vehicle.updated");
                    _vehicleDeletedChannel = OpenTopicQueueChannel(QueueName(VehicleDeletedQueueKey, "vehicle.deleted"), VehicleExchange, "vehicle.deleted");

                    Log.Information("RabbitMQ consumer connection and channels initialized successfully.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not connect to RabbitMQ for consuming. Check connection settings.");
                }
            }
        }

        /// <summary>
        /// Creates the channel for a queue: declares the queue (plus its
        /// .retry/.dlq dead-letter topology) and, when
        /// <paramref name="vehicleTopicRoutingKey"/> is set, binds it to the
        /// vehicle_events topic exchange under that routing key. The key is the
        /// event name, never the queue name: operators can rename a queue via
        /// RabbitMQ:Queues (the compose pattern SalesService uses) and must not
        /// thereby silently unbind it from the exchange. With a null key the
        /// queue stays on the default exchange, where the routing key IS the
        /// queue name (the SalesService publisher pattern: payment.received,
        /// order.status.changed, sales.*).
        /// </summary>
        private IModel? OpenQueueChannel(string queue, string? vehicleTopicRoutingKey)
        {
            try
            {
                var channel = _connection!.CreateModel();
                // One delivery in flight per channel: handlers are serialized, which
                // matches the CustomerService consumer (PR #11) and keeps the
                // check-then-act orderings in the DB handlers safe.
                channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                channel.QueueDeclare(queue: queue, durable: true, exclusive: false, autoDelete: false, arguments: null);
                EventRetryPolicy.DeclareRetryTopology(_connection!, queue, _retryTtlMs);
                if (vehicleTopicRoutingKey != null)
                {
                    channel.ExchangeDeclare(exchange: VehicleExchange, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
                    channel.QueueBind(queue: queue, exchange: VehicleExchange, routingKey: vehicleTopicRoutingKey);
                }
                return channel;
            }
            catch (Exception ex)
            {
                // Per-queue fail-soft: a single unopenable queue (stale declare
                // args, permissions) must not abort InitializeRabbitMQ's try and
                // silently null out the channels of every queue declared after
                // it - StartConsumingQueue just skips the null ones, so losing
                // only this queue is logged and visible in `docker compose logs`.
                Log.Error(ex, "Could not open consumer channel for queue {Queue}; this queue will NOT be consumed.", queue);
                return null;
            }
        }

        /// <summary>
        /// Topic-exchange variant of <see cref="OpenQueueChannel"/>: same
        /// declare + retry-topology + fail-soft contract, but binds to an
        /// arbitrary topic exchange instead of the vehicle one.
        /// </summary>
        private IModel? OpenTopicQueueChannel(string queue, string exchange, string routingKey)
        {
            try
            {
                var channel = _connection!.CreateModel();
                channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                channel.QueueDeclare(queue: queue, durable: true, exclusive: false, autoDelete: false, arguments: null);
                EventRetryPolicy.DeclareRetryTopology(_connection!, queue, _retryTtlMs);
                channel.ExchangeDeclare(exchange: exchange, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
                channel.QueueBind(queue: queue, exchange: exchange, routingKey: routingKey);
                return channel;
            }
            catch (Exception ex)
            {
                // Same per-queue fail-soft as OpenQueueChannel: losing only
                // this queue, never the channels declared after it.
                Log.Error(ex, "Could not open consumer channel for queue {Queue}; this queue will NOT be consumed.", queue);
                return null;
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

            StartConsumingQueue(_saleChannel, QueueName(SaleQueueKey, "sales.completed"),
                sp => sp.GetRequiredService<SaleCompletedConsumer>().HandleAsync);
            StartConsumingQueue(_reservationChannel, QueueName(ReservationQueueKey, "vehicle.reserved"),
                sp => sp.GetRequiredService<VehicleReservedConsumer>().HandleAsync);
            StartConsumingQueue(_testDriveChannel, QueueName(TestDriveQueueKey, "testdrive.scheduled"),
                sp => sp.GetRequiredService<TestDriveScheduledConsumer>().HandleAsync);
            StartConsumingQueue(_orderChannel, QueueName(OrderQueueKey, "order.created"),
                sp => sp.GetRequiredService<OrderCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_quoteChannel, QueueName(QuoteQueueKey, "quote.created"),
                sp => sp.GetRequiredService<QuoteCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_contractChannel, QueueName(ContractQueueKey, "contract.created"),
                sp => sp.GetRequiredService<ContractCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_customerCreatedChannel, QueueName(CustomerCreatedQueueKey, "customer.created"),
                sp => sp.GetRequiredService<CustomerCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_customerUpdatedChannel, QueueName(CustomerUpdatedQueueKey, "customer.updated"),
                sp => sp.GetRequiredService<CustomerUpdatedConsumer>().HandleAsync);
            StartConsumingQueue(_customerDeletedChannel, QueueName(CustomerDeletedQueueKey, "customer.deleted"),
                sp => sp.GetRequiredService<CustomerDeletedConsumer>().HandleAsync);
            StartConsumingQueue(_paymentReceivedChannel, QueueName(PaymentReceivedQueueKey, "payment.received"),
                sp => sp.GetRequiredService<PaymentReceivedConsumer>().HandleAsync);
            StartConsumingQueue(_orderStatusChangedChannel, QueueName(OrderStatusChangedQueueKey, "order.status.changed"),
                sp => sp.GetRequiredService<OrderStatusChangedConsumer>().HandleAsync);
            StartConsumingQueue(_vehicleCreatedChannel, QueueName(VehicleCreatedQueueKey, "vehicle.created"),
                sp => sp.GetRequiredService<VehicleCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_vehicleUpdatedChannel, QueueName(VehicleUpdatedQueueKey, "vehicle.updated"),
                sp => sp.GetRequiredService<VehicleUpdatedConsumer>().HandleAsync);
            StartConsumingQueue(_vehicleDeletedChannel, QueueName(VehicleDeletedQueueKey, "vehicle.deleted"),
                sp => sp.GetRequiredService<VehicleDeletedConsumer>().HandleAsync);

            Log.Information("Started consuming messages from all queues.");
        }

        /// <summary>
        /// Shared consume loop for one queue. Failure policy (docs/EVENTS.md):
        /// unparseable payload or no handler registered -> park in the DLQ
        /// (visible to operators, unlike the old ack-and-discard); handler
        /// threw -> republish to the .retry queue until RabbitMQ:MaxDeliveryAttempts
        /// is exhausted, then park in the DLQ. The old nack-and-requeue-on-every-
        /// error policy redelivered a poison message in a hot loop forever.
        /// </summary>
        private void StartConsumingQueue(
            IModel? channel,
            string queue,
            Func<IServiceProvider, Func<string, Task>> handlerFactory)
        {
            if (channel == null || !channel.IsOpen) return;

            var consumer = new EventingBasicConsumer(channel);

            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                // Malformed payloads never become parseable by retrying, so they
                // go straight to the DLQ instead of the old ack-and-discard.
                if (!IsJsonPayload(body))
                {
                    Log.Warning("Malformed payload on {Queue}; parked in DLQ. Body: {Message}", queue, Truncate(message));
                    SafePark(channel, ea, queue);
                    return;
                }

                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var handle = handlerFactory(scope.ServiceProvider);
                    await handle(message);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    var rounds = EventRetryPolicy.RetryRounds(ea, queue);
                    if (rounds >= _maxAttempts)
                    {
                        Log.Error(ex, "Message on {Queue} exhausted {Attempts} retries; parked in DLQ. Body: {Message}",
                            queue, _maxAttempts, Truncate(message));
                        SafePark(channel, ea, queue);
                        return;
                    }

                    Log.Warning(ex, "Handler for {Queue} failed (attempt {Attempt}/{Max}); retrying in {Ttl}ms. Body: {Message}",
                        queue, rounds + 1, _maxAttempts, _retryTtlMs, Truncate(message));
                    try
                    {
                        EventRetryPolicy.ScheduleRetry(_connection!, channel, ea, queue, _retryTtlMs);
                    }
                    catch (Exception retryEx)
                    {
                        // Republish itself failed (channel/connection dead): fall
                        // back to the old nack+requeue so the message is not lost.
                        Log.Error(retryEx, "Could not schedule retry for {Queue}; falling back to requeue.", queue);
                        try { channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true); }
                        catch (Exception nackEx) { Log.Error(nackEx, "Fallback nack failed for {Queue}.", queue); }
                    }
                }
            };

            channel.BasicConsume(queue: queue, autoAck: false, consumer: consumer);
            Log.Information("Started consuming from queue: {QueueName}", queue);
        }

        private void SafePark(IModel channel, BasicDeliverEventArgs ea, string queue)
        {
            try
            {
                EventRetryPolicy.ParkInDeadLetterQueue(channel, ea, queue);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not park message from {Queue} in DLQ; requeueing instead.", queue);
                try { channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true); }
                catch (Exception nackEx) { Log.Error(nackEx, "Fallback nack failed for {Queue}.", queue); }
            }
        }

        private static bool IsJsonPayload(byte[] body)
        {
            // Minimal guard: a JSON object must start with '{' after whitespace.
            // The per-field consumers also tolerate nulls, so this only catches
            // the poison shape (empty/garbage body) that used to ack-loop.
            foreach (var b in body)
            {
                if (b == (byte)' ') continue;
                if (b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n') continue;
                return b == (byte)'{';
            }
            return false;
        }

        private static string Truncate(string value, int max = 512) =>
            value.Length <= max ? value : value.Substring(0, max) + "...[truncated]";

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
            _orderChannel?.Close();
            _quoteChannel?.Close();
            _contractChannel?.Close();
            _customerCreatedChannel?.Close();
            _customerUpdatedChannel?.Close();
            _customerDeletedChannel?.Close();
            _paymentReceivedChannel?.Close();
            _orderStatusChangedChannel?.Close();
            _vehicleCreatedChannel?.Close();
            _vehicleUpdatedChannel?.Close();
            _vehicleDeletedChannel?.Close();
            _connection?.Close();
            Log.Information("RabbitMQ consumer connection closed.");
        }
    }
}
