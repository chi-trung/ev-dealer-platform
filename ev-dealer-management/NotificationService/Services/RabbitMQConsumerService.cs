using RabbitMQ.Client;

using Common.Events;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using NotificationService.DTOs;
using NotificationService.Consumers;
using NotificationService.Events;
using Serilog;

namespace NotificationService.Services
{
    public class RabbitMQConsumerService : IMessageConsumer, IDisposable
    {
        // Queue names, routing keys and exchange names all come from EventNames
        // (P3). They used to be string literals repeated in this file: once in
        // InitializeRabbitMQ to declare, once in StartConsuming to subscribe.
        // An edit to only one side compiled and booted cleanly — the service
        // declared queue A and consumed queue B, receiving nothing, with nothing
        // in the logs to say why. One declaration, two references, no gap.

        // RabbitMQ:Queues:{Key} overrides the default name. The key is stable
        // config vocabulary and is deliberately NOT the queue name: operators
        // rename a queue without changing which config key carries the rename.
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

        // Queue name resolved from configuration, paired with the channel opened
        // for it. Held in one place per queue so the declare half and the consume
        // half cannot drift: StartConsuming subscribes to this exact pair, so the
        // queue it listens on is by construction the queue that was declared.
        private readonly record struct Subscription(string QueueName, IModel? Channel);

        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly object _initLock = new object();
        private IConnection? _connection;

        // One channel per consumed queue: IModel is not thread-safe and a
        // delivery must be acked/rejected on its own channel. Resolved during
        // InitializeRabbitMQ and read again by StartConsuming.
        private Subscription _sale;
        private Subscription _reservation;
        private Subscription _testDrive;
        private Subscription _order;
        private Subscription _quote;
        private Subscription _contract;
        private Subscription _customerCreated;
        private Subscription _customerUpdated;
        private Subscription _customerDeleted;
        private Subscription _paymentReceived;
        private Subscription _orderStatusChanged;
        private Subscription _vehicleCreated;
        private Subscription _vehicleUpdated;
        private Subscription _vehicleDeleted;
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

                    // Each queue: resolve its name ONCE, declare it, and keep the
                    // pair. StartConsuming subscribes to these exact strings, so
                    // the queue it listens on is by construction the queue that
                    // was declared.
                    _sale = Open(QueueName(SaleQueueKey, EventNames.SaleCompleted), null);
                    _reservation = Open(QueueName(ReservationQueueKey, EventNames.VehicleReserved), EventNames.VehicleExchange, EventNames.VehicleReserved);
                    _testDrive = Open(QueueName(TestDriveQueueKey, EventNames.TestDriveScheduled), EventNames.VehicleExchange, EventNames.TestDriveScheduled);

                    // Sales lifecycle events published by SalesService to the
                    // default exchange, where the routing key IS the queue name.
                    _order = Open(QueueName(OrderQueueKey, EventNames.OrderCreated), null);
                    _quote = Open(QueueName(QuoteQueueKey, EventNames.QuoteCreated), null);
                    _contract = Open(QueueName(ContractQueueKey, EventNames.ContractCreated), null);

                    _customerCreated = Open(QueueName(CustomerCreatedQueueKey, EventNames.CustomerCreated), EventNames.CustomerExchange, EventNames.CustomerCreated);
                    _customerUpdated = Open(QueueName(CustomerUpdatedQueueKey, EventNames.CustomerUpdated), EventNames.CustomerExchange, EventNames.CustomerUpdated);
                    _customerDeleted = Open(QueueName(CustomerDeletedQueueKey, EventNames.CustomerDeleted), EventNames.CustomerExchange, EventNames.CustomerDeleted);

                    _paymentReceived = Open(QueueName(PaymentReceivedQueueKey, EventNames.PaymentReceived), null);
                    _orderStatusChanged = Open(QueueName(OrderStatusChangedQueueKey, EventNames.OrderStatusChanged), null);

                    _vehicleCreated = Open(QueueName(VehicleCreatedQueueKey, EventNames.VehicleCreated), EventNames.VehicleExchange, EventNames.VehicleCreated);
                    _vehicleUpdated = Open(QueueName(VehicleUpdatedQueueKey, EventNames.VehicleUpdated), EventNames.VehicleExchange, EventNames.VehicleUpdated);
                    _vehicleDeleted = Open(QueueName(VehicleDeletedQueueKey, EventNames.VehicleDeleted), EventNames.VehicleExchange, EventNames.VehicleDeleted);

                    Log.Information("RabbitMQ consumer connection and channels initialized successfully.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not connect to RabbitMQ for consuming. Check connection settings.");
                }
            }
        }

        /// <summary>
        /// Declares one queue and returns it paired with its channel. Declares the
        /// .retry/.dlq dead-letter topology too. When <paramref name="exchange"/>
        /// is set, also declares that topic exchange and binds the queue under
        /// <paramref name="routingKey"/>; with a null exchange the queue stays on
        /// the default exchange, where the routing key IS the queue name (the
        /// SalesService publisher pattern: payment.received, order.status.changed,
        /// sales.*).
        ///
        /// The routing key is always the EVENT name, never the resolved queue
        /// name: operators rename a queue via RabbitMQ:Queues and that must not
        /// silently unbind it from the exchange.
        ///
        /// The returned QueueName is what the caller keeps and hands to
        /// StartConsumingQueue, so the subscribed name cannot drift from the
        /// declared one.
        ///
        /// Per-queue fail-soft: a single unopenable queue (stale declare args,
        /// permissions) must not abort InitializeRabbitMQ's try and null out the
        /// channels of every queue declared after it. StartConsumingQueue skips
        /// the null channel, so losing only this queue is logged and visible in
        /// `docker compose logs`.
        /// </summary>
        private Subscription Open(string queue, string? exchange = null, string? routingKey = null)
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
                if (exchange != null && routingKey != null)
                {
                    channel.ExchangeDeclare(exchange: exchange, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
                    channel.QueueBind(queue: queue, exchange: exchange, routingKey: routingKey);
                }
                return new Subscription(queue, channel);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not open consumer channel for queue {Queue}; this queue will NOT be consumed.", queue);
                return new Subscription(queue, null);
            }
        }

        public void StartConsuming()
        {
            if (!IsConnected)
            {
                Log.Warning("RabbitMQ connection is not open. Attempting to re-initialize.");
                // Tear the previous generation down first. InitializeRabbitMQ
                // assigns fresh channels to the same fields, so without this
                // every reconnect would leak the 14 old channels (and any
                // consumers still attached to them) for the life of the process.
                Dispose();
                InitializeRabbitMQ();
                if (!IsConnected)
                {
                    Log.Error("Failed to start consuming: RabbitMQ connection is still not open.");
                    return;
                }
            }

            StartConsumingQueue(_sale, sp => sp.GetRequiredService<SaleCompletedConsumer>().HandleAsync);
            StartConsumingQueue(_reservation, sp => sp.GetRequiredService<VehicleReservedConsumer>().HandleAsync);
            StartConsumingQueue(_testDrive, sp => sp.GetRequiredService<TestDriveScheduledConsumer>().HandleAsync);
            StartConsumingQueue(_order, sp => sp.GetRequiredService<OrderCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_quote, sp => sp.GetRequiredService<QuoteCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_contract, sp => sp.GetRequiredService<ContractCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_customerCreated, sp => sp.GetRequiredService<CustomerCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_customerUpdated, sp => sp.GetRequiredService<CustomerUpdatedConsumer>().HandleAsync);
            StartConsumingQueue(_customerDeleted, sp => sp.GetRequiredService<CustomerDeletedConsumer>().HandleAsync);
            StartConsumingQueue(_paymentReceived, sp => sp.GetRequiredService<PaymentReceivedConsumer>().HandleAsync);
            StartConsumingQueue(_orderStatusChanged, sp => sp.GetRequiredService<OrderStatusChangedConsumer>().HandleAsync);
            StartConsumingQueue(_vehicleCreated, sp => sp.GetRequiredService<VehicleCreatedConsumer>().HandleAsync);
            StartConsumingQueue(_vehicleUpdated, sp => sp.GetRequiredService<VehicleUpdatedConsumer>().HandleAsync);
            StartConsumingQueue(_vehicleDeleted, sp => sp.GetRequiredService<VehicleDeletedConsumer>().HandleAsync);

            Log.Information("Started consuming messages from all queues.");
        }

        /// <summary>
        /// Shared consume loop for one queue. Takes the whole <see cref="Subscription"/>
        /// rather than (channel, name) so the queue it subscribes to is the same
        /// value Open declared. Failure policy (docs/EVENTS.md): unparseable
        /// payload or no handler registered -> park in the DLQ (visible to
        /// operators, unlike the old ack-and-discard); handler threw -> republish
        /// to the .retry queue until RabbitMQ:MaxDeliveryAttempts is exhausted,
        /// then park in the DLQ. The old nack-and-requeue-on-every-error policy
        /// redelivered a poison message in a hot loop forever.
        /// </summary>
        private void StartConsumingQueue(
            Subscription subscription,
            Func<IServiceProvider, Func<string, Task>> handlerFactory)
        {
            var channel = subscription.Channel;
            if (channel == null || !channel.IsOpen) return;
            var queue = subscription.QueueName;

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

        /// <summary>
        /// Whether the broker connection is currently usable. Read on every
        /// poll by <see cref="RabbitMQConsumerHostedService"/>; also reflects a
        /// connection that InitializeRabbitMQ failed to open (null).
        /// </summary>
        public bool IsConnected => _connection is { IsOpen: true };

        /// <summary>
        /// The live broker connection, for tests that need to ask the BROKER a
        /// question about what this service declared and subscribed to — the
        /// only oracle that catches a declare/subscribe name drift. internal
        /// rather than public so the connection never becomes part of the
        /// service's surface; NotificationService.csproj grants
        /// InternalsVisibleTo to the test assembly for this.
        /// </summary>
        internal IConnection? Connection => _connection;

        public void StopConsuming()
        {
            Log.Information("Stopping RabbitMQ consumer.");
            Dispose();
        }

        public void Dispose()
        {
            // Every Close is guarded: this runs on the shutdown path, where the
            // broker may already be gone, and an AlreadyClosedException thrown
            // here would abort host shutdown and skip the remaining services.
            CloseQuietly(_sale.Channel);
            CloseQuietly(_reservation.Channel);
            CloseQuietly(_testDrive.Channel);
            CloseQuietly(_order.Channel);
            CloseQuietly(_quote.Channel);
            CloseQuietly(_contract.Channel);
            CloseQuietly(_customerCreated.Channel);
            CloseQuietly(_customerUpdated.Channel);
            CloseQuietly(_customerDeleted.Channel);
            CloseQuietly(_paymentReceived.Channel);
            CloseQuietly(_orderStatusChanged.Channel);
            CloseQuietly(_vehicleCreated.Channel);
            CloseQuietly(_vehicleUpdated.Channel);
            CloseQuietly(_vehicleDeleted.Channel);
            CloseQuietly(_connection);
            Log.Information("RabbitMQ consumer connection closed.");
        }

        private static void CloseQuietly(IModel? channel)
        {
            try { channel?.Close(); } catch { }
        }

        private static void CloseQuietly(IConnection? connection)
        {
            try { connection?.Close(); } catch { }
        }
    }
}
