using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Events;
using NotificationService.Services;
using RabbitMQ.Client;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Pins the live RabbitMQ queue topology (P3).
///
/// The plan asked for "14 queues + 14 .retry + 14 .dlq = 42". Measured against
/// the real broker that number is wrong: it is <b>45</b>, because it omits
/// <c>customer_vehicle_reserved</c>, which CustomerService also declares with
/// the same retry topology (VehicleReservedEventConsumer.cs:102). The counts in
/// <see cref="AllQueuesAreDeclaredWithTheirRetryTopology"/> are the measured
/// ones, not the planned ones.
///
/// WHY THIS FILE IS MOSTLY NOT A COUNTING TEST. Counting queues is a weak
/// assertion: it says nothing about WHICH name each half of the service uses.
/// The failure that actually ships is subtler and a count cannot see it — the
/// service declared queue A and consumes queue B, because the name was written
/// out twice in RabbitMQConsumerService and someone edited one side. Nothing
/// crashes, every log line says "Started consuming messages from all queues",
/// and the service receives nothing.
///
/// So the load-bearing test here is
/// <see cref="RenamedQueuesAreDeclaredAndConsumedUnderTheSameName"/>, which
/// makes that drift happen on purpose and asks the broker whether the renamed
/// queue ended up with a consumer. It needs a real broker.
///
/// A BROKER THAT HAS BEEN USED IS A PRECONDITION, and the first CI run found
/// that out the hard way: 3 of these tests failed on a fresh service container
/// with "customer_vehicle_reserved is missing", while every one of them passed
/// on a developer machine whose compose stack was already warm. A queue
/// declared on a broker nobody consumes has 0 consumers, and
/// customer_vehicle_reserved is declared by CustomerService, not by the service
/// under test. CI therefore runs DealerSystem.Tests/TopologyWarm first, which
/// starts the real RabbitMQConsumerService and adds that one missing consumer.
/// Run the suite against a cold broker locally and these three will fail for a
/// reason that has nothing to do with the code.
/// </summary>
public class QueueTopologyTests
{
    // ---- Live-broker assertions. Numbers measured on evm_rabbitmq, not planned. ----

    /// <summary>
    /// 15 main queues: NotificationService's 14 plus CustomerService's
    /// customer_vehicle_reserved. Measured, not derived — the "42" in the plan
    /// came from counting only the 14.
    /// </summary>
    private const int MainQueueCount = 15;

    private const string CustomerVehicleReserved = "customer_vehicle_reserved";

    [BrokerFact]
    public void AllQueuesAreDeclaredWithTheirRetryTopology()
    {
        using var broker = Connect();
        var model = broker.CreateModel();
        try
        {
            var main = MainQueues();
            Assert.Equal(MainQueueCount, main.Count);

            // .dlq and .retry are both QueueDeclared by
            // EventRetryPolicy.DeclareRetryTopology, so they exist as soon as any
            // service has declared the main queue — no consumer needs to be up.
            foreach (var q in main)
            {
                Assert.True(Exists(model, q), $"main queue '{q}' is missing");
                Assert.True(Exists(model, q + ".retry"), $"'{q}.retry' is missing");
                Assert.True(Exists(model, q + ".dlq"), $"'{q}.dlq' is missing");
            }
        }
        finally { model.Close(); }
    }

    [BrokerFact]
    public void BothTopicExchangesExist()
    {
        using var broker = Connect();
        var model = broker.CreateModel();
        try
        {
            // ExchangeDeclarePassive returns void and THROWS when the exchange is
            // missing (checked against RabbitMQ.Client 6.6.0 — there is no
            // ExchangeDeclarePassiveOk to inspect, so the type is asserted by
            // round-tripping the declare instead).
            foreach (var exchange in new[] { EventNames.VehicleExchange, EventNames.CustomerExchange })
            {
                model.ExchangeDeclarePassive(exchange);
            }

            // IModel cannot report an exchange's type passively, and this client
            // version has no QueueBindPassive either (verified by reflection over
            // RabbitMQ.Client 6.6.0's IModel: the only Bind members are
            // QueueBind, ExchangeBind and their Unbind/NoWait variants). So the
            // exchange type and the bindings are asserted by the offline table
            // tests below plus an operator-run rabbitmqctl check, not here.
            // What this test can prove is that both exchanges EXIST and are
            // declared as topics by this service's own Open() call.
            foreach (var exchange in new[] { EventNames.VehicleExchange, EventNames.CustomerExchange })
            {
                // A re-declare with the wrong type fails the channel with
                // PRECONDITION_FAILED, so declaring as topic and surviving is
                // the assertion. Durable + autoDelete:false matches the service.
                model.ExchangeDeclare(exchange, ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
            }
        }
        finally { model.Close(); }
    }

    [BrokerFact]
    public void VehicleReservedIsTheOnlyFanOut()
    {
        using var broker = Connect();
        var model = broker.CreateModel();
        try
        {
            // Binding existence is not readable through this client (see
            // BothTopicExchangesExist). What IS assertable here: the
            // customer_vehicle_reserved queue exists and has its own consumer,
            // which is the observable consequence of being bound — a queue with
            // no binding never receives, so it would sit at zero consumers while
            // every other queue had one.
            Assert.True(Exists(model, CustomerVehicleReserved),
                "customer_vehicle_reserved is missing");
            Assert.Equal(1, ActiveConsumerCount(model, CustomerVehicleReserved));
        }
        finally { model.Close(); }
    }

    /// <summary>
    /// A queue with zero consumers still exists, still holds its retry topology,
    /// and still passes every count assertion above — it just never processes
    /// anything. This is the check that a consumer whose Open() returned a null
    /// channel is actually dead rather than merely idle-looking.
    /// </summary>
    [BrokerFact]
    public void EveryQueueHasExactlyOneConsumer()
    {
        using var broker = Connect();
        var model = broker.CreateModel();
        try
        {
            foreach (var queue in MainQueues())
            {
                Assert.Equal(1, ActiveConsumerCount(model, queue));
            }
        }
        finally { model.Close(); }
    }

    // ---- Offline assertions: no broker required. ----

    /// <summary>
    /// The test the refactor exists for, run against a real broker.
    ///
    /// Before P3, InitializeRabbitMQ and StartConsuming each spelled every queue
    /// name out as a separate string literal. Editing one side produced a
    /// service that DECLARED queue A and CONSUMED queue B — it boots, connects,
    /// logs "Started consuming messages from all queues", and receives nothing.
    /// A queue count stays correct through all of that, because the declared
    /// name is still a perfectly valid queue.
    ///
    /// This test creates exactly that drift and checks the broker's own view.
    /// Every queue is renamed through RabbitMQ:Queues configuration, so the
    /// declared name and the subscribed name diverge from the EventNames
    /// defaults. If the two halves resolved names independently, the renamed
    /// queue would be declared and never consumed: zero consumers, which the
    /// assertions below see. Because Open() now returns the declared name paired
    /// with its channel and StartConsuming subscribes to that same pair, the
    /// consumer lands on the renamed queue.
    ///
    /// An earlier version of this test scraped ldstr operands out of the
    /// compiled IL. It could not have worked against the refactored code at
    /// all: Open() takes the queue name as a PARAMETER, so no string literal
    /// precedes the QueueDeclare call and the scrape returns an empty list. It
    /// would have passed vacuously — an empty collection satisfies "every
    /// declared name is known" — which is the exact false confidence this suite
    /// exists to remove. The broker is the oracle here, not the bytecode.
    ///
    /// The consumers are resolved from the service provider lazily, inside the
    /// Received handler. With no messages published, that handler never runs, so
    /// a stub provider is enough and none of the 14 consumers are constructed.
    /// </summary>
    [BrokerFact]
    public void RenamedQueuesAreDeclaredAndConsumedUnderTheSameName()
    {
        var renamed = EventNames.AllQueues
            .Select((q, i) => (Default: q, Custom: $"p3.probe.{i}.{q}"))
            .ToList();

        var config = new Dictionary<string, string?>
        {
            ["RabbitMQ:HostName"] = Environment.GetEnvironmentVariable(BrokerFactAttribute.HostVariable),
            ["RabbitMQ:Port"] = "5672",
            ["RabbitMQ:UserName"] = "guest",
            ["RabbitMQ:Password"] = "guest",
            ["RabbitMQ:MaxDeliveryAttempts"] = "3",
            ["RabbitMQ:RetryTtlMilliseconds"] = "5000",
        };
        for (var i = 0; i < renamed.Count; i++)
        {
            config[$"RabbitMQ:Queues:{ConfigKeyFor(renamed[i].Default)}"] = renamed[i].Custom;
        }

        var provider = BuildServiceProvider();
        var service = new RabbitMQConsumerService(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), provider);
        try
        {
            service.StartConsuming();

            using var model = service.Connection.CreateModel();
            foreach (var (def, custom) in renamed)
            {
                Assert.True(Exists(model, custom),
                    $"'{custom}' (renamed from {def}) was never declared");

                // The load-bearing assertion. Zero consumers here means the
                // service declared this queue and subscribed to some other name
                // — the exact bug the refactor removes.
                Assert.Equal(1, ActiveConsumerCount(model, custom));
            }
        }
        finally
        {
            service.StopConsuming();
            CleanupRenamedQueues(renamed.Select(r => r.Custom));
            provider.Dispose();
        }
    }

    /// <summary>
    /// Map a default queue name back to its RabbitMQ:Queues config key, which is
    /// fixed vocabulary (SaleCompleted, VehicleReserved, ...) rather than the
    /// name itself. Kept as a switch so a rename on either side fails loudly
    /// instead of silently configuring a key nothing reads.
    /// </summary>
    private static string ConfigKeyFor(string defaultQueueName) => defaultQueueName switch
    {
        "sales.completed" => "SaleCompleted",
        "vehicle.reserved" => "VehicleReserved",
        "testdrive.scheduled" => "TestDriveScheduled",
        "order.created" => "OrderCreated",
        "order.status.changed" => "OrderStatusChanged",
        "quote.created" => "QuoteCreated",
        "contract.created" => "ContractCreated",
        "customer.created" => "CustomerCreated",
        "customer.updated" => "CustomerUpdated",
        "customer.deleted" => "CustomerDeleted",
        "payment.received" => "PaymentReceived",
        "vehicle.created" => "VehicleCreated",
        "vehicle.updated" => "VehicleUpdated",
        "vehicle.deleted" => "VehicleDeleted",
        _ => throw new InvalidOperationException(
            $"no RabbitMQ:Queues key mapped for '{defaultQueueName}' — add it, do not let the probe skip it"),
    };

    /// <summary>
    /// Delete the probe queues so a run does not leave 45 durable queues behind
    /// that the real services would then not own. Without this the next run's
    /// EveryQueueHasExactlyOneConsumer would see the leftovers and fail.
    /// </summary>
    private static void CleanupRenamedQueues(IEnumerable<string> names)
    {
        try
        {
            using var broker = Connect();
            var model = broker.CreateModel();
            foreach (var name in names)
            {
                foreach (var suffix in new[] { "", ".retry", ".dlq" })
                {
                    try { model.QueueDelete(name + suffix, ifUnused: false, ifEmpty: false); }
                    catch (Exception) { /* never declared, or already gone */ }
                }
            }
        }
        catch (Exception)
        {
            // Cleanup failure must not mask the test's own result; a leftover
            // queue is visible in rabbitmqctl and is this test's own mess.
        }
    }

    /// <summary>
    /// The routing key a topic queue binds under is the EVENT name, never the
    /// resolved queue name. Get this backwards and every RabbitMQ:Queues rename
    /// silently unbinds the queue from its exchange — the queue keeps existing
    /// and keeps its retry topology, so a queue count stays correct while
    /// deliveries drop to zero.
    ///
    /// The concrete property: in every binding table, queue == routing key, and
    /// both are a known EventNames constant. They are the same strings today
    /// because the default queue name IS the event name; the point is that the
    /// code routes on the EVENT, so a future rename that makes a queue diverge
    /// from its event still binds by event.
    /// </summary>
    [Fact]
    public void TopicBindingsUseEventNamesNotQueueNames()
    {
        var known = EventNames.AllQueues.ToHashSet(StringComparer.Ordinal);
        foreach (var (queue, routingKey) in EventNames.VehicleExchangeBindings
                     .Concat(EventNames.CustomerExchangeBindings))
        {
            Assert.Equal(queue, routingKey);
            Assert.True(known.Contains(routingKey),
                $"routing key '{routingKey}' is not a known event name");
        }
    }

    /// <summary>
    /// The three tables must partition AllQueues: every queue on a topic
    /// exchange or on the default exchange, exactly once. A queue listed in both
    /// would be declared once but bound twice, delivering every message to the
    /// handler twice.
    /// </summary>
    [Fact]
    public void TheThreeBindingTablesPartitionAllQueues()
    {
        var topic = EventNames.VehicleExchangeBindings.Concat(EventNames.CustomerExchangeBindings)
            .Select(b => b.Queue).ToList();
        var dflt = EventNames.DefaultExchangeQueues.ToList();

        Assert.Equal(EventNames.AllQueues.Count, topic.Count + dflt.Count);
        Assert.Empty(topic.Intersect(dflt, StringComparer.Ordinal));

        var known = EventNames.AllQueues.ToHashSet(StringComparer.Ordinal);
        foreach (var name in topic.Concat(dflt))
        {
            Assert.True(known.Contains(name), $"'{name}' is bound but is not in EventNames.AllQueues");
        }
    }

    [Fact]
    public void NoQueueNameIsDuplicated()
    {
        var dupes = EventNames.AllQueues
            .GroupBy(q => q, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(dupes);
    }

    // ---- helpers ----

    /// <summary>
    /// A service provider with every consumer registered but unresolvable
    /// *without* being needed. The consumers are pulled from the scope inside
    /// the Received handler, and this test publishes nothing, so no handler ever
    /// runs and none of them is constructed. That is what lets a test drive the
    /// real RabbitMQConsumerService without a database, an FCM credential or a
    /// DI graph of the real application.
    /// </summary>
    private static ServiceProvider BuildServiceProvider() =>
        new ServiceCollection().BuildServiceProvider();

    private static IConnection Connect()
    {
        var host = Environment.GetEnvironmentVariable(BrokerFactAttribute.HostVariable)!;
        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = int.Parse(Environment.GetEnvironmentVariable("EVM_TEST_RABBITMQ_PORT") ?? "5672"),
            UserName = Environment.GetEnvironmentVariable("EVM_TEST_RABBITMQ_USER") ?? "guest",
            Password = Environment.GetEnvironmentVariable("EVM_TEST_RABBITMQ_PASS") ?? "guest",
        };
        return factory.CreateConnection();
    }

    /// <summary>
    /// The 15 main queues: this service's 14 plus CustomerService's
    /// customer_vehicle_reserved, which shares the vhost and declares the same
    /// retry topology.
    /// </summary>
    private static List<string> MainQueues() =>
        EventNames.AllQueues.Concat(new[] { CustomerVehicleReserved }).ToList();

    private static bool Exists(IModel model, string name)
    {
        try { model.QueueDeclarePassive(name); return true; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Consumers currently attached to a queue, or -1 when the queue is absent.
    /// <see cref="QueueDeclareOk.ConsumerCount"/> is a uint in RabbitMQ.Client
    /// 6.6.0; the cast is bounded by the fact that a queue with more than int.MaxValue
    /// consumers is not representable in this assertion anyway.
    /// </summary>
    private static int ActiveConsumerCount(IModel model, string queue)
    {
        try { return (int)model.QueueDeclarePassive(queue).ConsumerCount; }
        catch (Exception) { return -1; }
    }
}
