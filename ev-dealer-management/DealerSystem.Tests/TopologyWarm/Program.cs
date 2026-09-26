// P3: warms a bare RabbitMQ broker into the state the topology tests assert.
//
// WHY THIS EXISTS. QueueTopologyTests has three tests that cannot pass against
// an empty broker:
//
//   AllQueuesAreDeclaredWithTheirRetryTopology  - needs 15 queues declared
//   EveryQueueHasExactlyOneConsumer             - needs 15 LIVE consumers
//   VehicleReservedIsTheOnlyFanOut              - needs CustomerService's queue
//                                                 to exist AND have a consumer
//
// Declaring queues is not enough. Measured on 2026-09-26: a queue declared
// through the management API and never consumed reports 0 consumers. A
// consumer only exists while a channel with BasicConsume is open, so the only
// honest way to produce one is to actually consume. That is why this is a
// long-running process rather than a setup step: the consumers have to still be
// attached when `dotnet test` runs, not when the warm-up finished.
//
// It was found the hard way: the first CI run of PR #146 failed 3 tests with
// "customer_vehicle_reserved is missing" while 311 passed and 0 skipped. The
// local machine had all 9 compose containers warm, so every mutation run here
// was green. A test that can only pass on a developer machine is a test that
// will be deleted the first time it is inconvenient.
//
// NO DATABASE, NO FIREBASE CREDENTIAL, NO JWT. That is not an assumption:
// NotificationService resolves IFcmService lazily inside each consumer's
// Received handler, and its Migrate() call is wrapped in a fail-soft try, so
// the service boots with an empty service provider and an unreachable DB.
//
// EXIT CODES. 0 = broker warm, 1 = preconditions unmet, 2 = broker unreachable.
// CI treats 2 as a hard failure rather than skipping, so a broker that dies
// turns the job red instead of quietly reducing the test count.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Events;
using NotificationService.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var host = Environment.GetEnvironmentVariable("EVM_TEST_RABBITMQ_HOST") ?? "localhost";
var port = int.Parse(Environment.GetEnvironmentVariable("EVM_TEST_RABBITMQ_PORT") ?? "5672");
var user = Environment.GetEnvironmentVariable("EVM_TEST_RABBITMQ_USER") ?? "guest";
var pass = Environment.GetEnvironmentVariable("EVM_TEST_RABBITMQ_PASS") ?? "guest";
var retryTtl = int.Parse(Environment.GetEnvironmentVariable("EVM_TEST_RABBITMQ_RETRY_TTL_MS") ?? "5000");

// NotificationService's own resolver for RabbitMQ:Queues:{Key}. Reproduced
// here rather than reused because that method is private to the service and
// widening its visibility for a test tool would be a worse trade than 14 lines
// of explicit mapping. The mapping is asserted against the broker below: every
// one of these names must show up declared, so a key that drifts from
// EventNames turns into a red run instead of a silently unwarmed queue.
static string KeyFor(string queue) => queue switch
{
    EventNames.SaleCompleted => "SaleCompleted",
    EventNames.VehicleReserved => "VehicleReserved",
    EventNames.TestDriveScheduled => "TestDriveScheduled",
    EventNames.OrderCreated => "OrderCreated",
    EventNames.OrderStatusChanged => "OrderStatusChanged",
    EventNames.QuoteCreated => "QuoteCreated",
    EventNames.ContractCreated => "ContractCreated",
    EventNames.CustomerCreated => "CustomerCreated",
    EventNames.CustomerUpdated => "CustomerUpdated",
    EventNames.CustomerDeleted => "CustomerDeleted",
    EventNames.PaymentReceived => "PaymentReceived",
    EventNames.VehicleCreated => "VehicleCreated",
    EventNames.VehicleUpdated => "VehicleUpdated",
    EventNames.VehicleDeleted => "VehicleDeleted",
    _ => throw new InvalidOperationException($"no RabbitMQ:Queues key for '{queue}'"),
};

var settings = new Dictionary<string, string?>
{
    ["RabbitMQ:HostName"] = host,
    ["RabbitMQ:Port"] = port.ToString(),
    ["RabbitMQ:UserName"] = user,
    ["RabbitMQ:Password"] = pass,
    ["RabbitMQ:MaxDeliveryAttempts"] = "3",
    ["RabbitMQ:RetryTtlMilliseconds"] = retryTtl.ToString(),
};
foreach (var queue in EventNames.AllQueues)
{
    settings[$"RabbitMQ:Queues:{KeyFor(queue)}"] = queue;
}

var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

// An EMPTY provider on purpose. The 14 consumers take IFcmService and
// IDeviceTokenRegistry through their constructors but the service resolves them
// from the scope inside the Received handler. Nothing is published here, so no
// handler runs and none of the 14 is ever constructed.
using var provider = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
    .BuildServiceProvider();

IConnection broker;
try
{
    var factory = new ConnectionFactory
    {
        HostName = host,
        Port = port,
        UserName = user,
        Password = pass,
    };
    broker = factory.CreateConnection();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[warm] cannot reach broker at {host}:{port} - {ex.Message}");
    return 2;
}

// The real service, not a reimplementation of it. This is the same class
// StartConsuming runs inside NotificationService, so what it declares here is
// what the service declares in production.
var consumerService = new RabbitMQConsumerService(configuration, provider);
consumerService.StartConsuming();

// CustomerService's queue. NotificationService never declares this one, which
// is exactly why it was missing on a clean broker: the fan-out has two legs
// (VehicleReservedEventConsumer.cs:19) and only one of them lives in this
// service. Routing key is vehicle.reserved on the shared vehicle_events topic
// exchange, taken from the same source as the real consumer.
const string customerQueue = "customer_vehicle_reserved";
IModel? customerChannel = null;
try
{
    customerChannel = broker.CreateModel();
    customerChannel.ExchangeDeclare(
        exchange: EventNames.VehicleExchange, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
    customerChannel.QueueDeclare(queue: customerQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
    customerChannel.QueueBind(queue: customerQueue, exchange: EventNames.VehicleExchange, routingKey: EventNames.VehicleReserved);

    // Declared through Common's shared policy so the .retry/.dlq arguments are
    // identical to the real consumer's. A warm queue with a subtly different
    // TTL would make EveryQueueHasExactlyOneConsumer pass for the wrong reason.
    Common.Events.EventRetryPolicy.DeclareRetryTopology(broker, customerQueue, retryTtl);
    customerChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

    // Swallow and drop. A topology warm-up must not act on messages: if a
    // previous run left a message on the queue, ack-ing it would consume real
    // traffic and BasicNack would push it onto the .retry chain and start a
    // redelivery loop. Holding the consumer without touching the payload keeps
    // this side-effect free; the tests only ever read consumer counts.
    var sink = new EventingBasicConsumer(customerChannel);
    sink.Received += (_, ea) => customerChannel?.BasicAck(ea.DeliveryTag, multiple: false);
    customerChannel.BasicConsume(customerQueue, autoAck: false, sink);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[warm] could not open CustomerService's consumer: {ex.Message}");
    return 1;
}

// Report what the BROKER sees, not what we asked for. If the two disagree the
// process is a no-op and CI should know now rather than through three red tests.
var declared = new List<string>();
var withConsumer = new List<string>();
using (var probe = broker.CreateModel())
{
    foreach (var queue in EventNames.AllQueues.Append(customerQueue))
    {
        try
        {
            probe.QueueDeclarePassive(queue);
            declared.Add(queue);
            if (probe.QueueDeclarePassive(queue).ConsumerCount > 0) withConsumer.Add(queue);
        }
        catch (Exception) { /* left as absent; reported below */ }
    }
}

var wanted = EventNames.AllQueues.Count + 1;
Console.WriteLine($"[warm] {host}:{port} declared {declared.Count}/{wanted}, consumers on {withConsumer.Count}/{wanted}");
if (declared.Count != wanted || withConsumer.Count != wanted)
{
    Console.Error.WriteLine(
        $"[warm] INCOMPLETE - missing: {string.Join(", ", EventNames.AllQueues.Append(customerQueue).Except(declared))}");
    return 1;
}

Console.WriteLine("[warm] broker is warm; holding consumers open until killed");
Console.Out.Flush();

// Hold every channel open. BasicConsume is not a subscription object that
// outlives its channel, so exiting here would drop all 15 consumers and put
// the tests back in exactly the failing state this tool exists to prevent.
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stopping.Cancel();
try
{
    Task.Delay(Timeout.Infinite, stopping.Token).GetAwaiter().GetResult();
}
catch (OperationCanceledException) { /* shutting down on signal */ }

consumerService.StopConsuming();
try { customerChannel?.Close(); } catch (Exception) { /* already gone */ }
Console.WriteLine("[warm] consumers closed");
return 0;
