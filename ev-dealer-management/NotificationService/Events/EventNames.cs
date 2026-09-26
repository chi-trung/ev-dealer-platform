namespace NotificationService.Events;

/// <summary>
/// Canonical routing keys, queue names and exchanges consumed by NotificationService.
/// See docs/EVENTS.md for the full topology.
///
/// Why this file exists (P3): the 14 queue names used to be string literals
/// written out TWICE in <c>RabbitMQConsumerService</c> — once in
/// <c>InitializeRabbitMQ</c> (which declares them) and once in
/// <c>StartConsuming</c> (which subscribes to them). Editing one side only
/// compiles, boots, and connects fine: the service declares queue A and
/// consumes queue B. Nothing in the logs says so and no test caught it.
/// Declaring each name once and referencing it from both sides makes that
/// class of edit a compile error instead.
///
/// These are the DEFAULT names. <c>RabbitMQ:Queues:{Key}</c> in configuration
/// still overrides the queue name (that is the rename mechanism operators are
/// meant to use), so the constants below are what topology tests assert against
/// and what an unconfigured deployment actually gets.
/// </summary>
public static class EventNames
{
    /// <summary>Topic exchange carrying vehicle.* and testdrive.* events.</summary>
    public const string VehicleExchange = "vehicle_events";

    /// <summary>Topic exchange carrying customer.* events.</summary>
    public const string CustomerExchange = "customer_events";

    // ---- Queue names. Also used as routing keys on the default exchange ----

    public const string SaleCompleted = "sales.completed";
    public const string VehicleReserved = "vehicle.reserved";
    public const string TestDriveScheduled = "testdrive.scheduled";
    public const string OrderCreated = "order.created";
    public const string OrderStatusChanged = "order.status.changed";
    public const string QuoteCreated = "quote.created";
    public const string ContractCreated = "contract.created";
    public const string CustomerCreated = "customer.created";
    public const string CustomerUpdated = "customer.updated";
    public const string CustomerDeleted = "customer.deleted";
    public const string PaymentReceived = "payment.received";
    public const string VehicleCreated = "vehicle.created";
    public const string VehicleUpdated = "vehicle.updated";
    public const string VehicleDeleted = "vehicle.deleted";

    /// <summary>
    /// Every queue this service declares, in declaration order. The topology
    /// test asserts this set against the broker, so adding a consumer means
    /// adding a constant here and a channel above it — the test fails otherwise.
    /// </summary>
    public static readonly IReadOnlyList<string> AllQueues = new[]
    {
        SaleCompleted,
        VehicleReserved,
        TestDriveScheduled,
        OrderCreated,
        QuoteCreated,
        ContractCreated,
        CustomerCreated,
        CustomerUpdated,
        CustomerDeleted,
        PaymentReceived,
        OrderStatusChanged,
        VehicleCreated,
        VehicleUpdated,
        VehicleDeleted,
    };

    /// <summary>
    /// Queues bound to <see cref="VehicleExchange"/> under an event-name routing
    /// key. Renaming one of these queues via RabbitMQ:Queues does not unbind it:
    /// the binding always uses the routing key from the table, never the queue name.
    /// </summary>
    public static readonly IReadOnlyList<(string Queue, string RoutingKey)> VehicleExchangeBindings = new[]
    {
        (VehicleReserved, VehicleReserved),
        (TestDriveScheduled, TestDriveScheduled),
        (VehicleCreated, VehicleCreated),
        (VehicleUpdated, VehicleUpdated),
        (VehicleDeleted, VehicleDeleted),
    };

    /// <summary>
    /// Queues bound to <see cref="CustomerExchange"/>, same bind-by-event-key rule.
    /// </summary>
    public static readonly IReadOnlyList<(string Queue, string RoutingKey)> CustomerExchangeBindings = new[]
    {
        (CustomerCreated, CustomerCreated),
        (CustomerUpdated, CustomerUpdated),
        (CustomerDeleted, CustomerDeleted),
    };

    /// <summary>
    /// Queues left on the DEFAULT exchange, where the routing key IS the queue
    /// name. Published by SalesService with the queue name as the routing key:
    /// sales.completed, order.created, order.status.changed, payment.received,
    /// quote.created, contract.created.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExchangeQueues = new[]
    {
        SaleCompleted,
        OrderCreated,
        OrderStatusChanged,
        PaymentReceived,
        QuoteCreated,
        ContractCreated,
    };
}
