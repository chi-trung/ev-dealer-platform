namespace CustomerService.Events;

/// <summary>
/// Canonical routing keys and the shared topic exchanges used by CustomerService.
/// Customer lifecycle events are fanned out on "customer_events"; test-drive
/// scheduling rides the vehicle domain exchange "vehicle_events".
/// See docs/EVENTS.md for the full topology.
/// </summary>
public static class EventNames
{
    /// <summary>Topic exchange carrying customer.* events.</summary>
    public const string CustomerExchange = "customer_events";

    /// <summary>Shared topic exchange for vehicle/test-drive domain events.</summary>
    public const string VehicleExchange = "vehicle_events";

    public const string CustomerCreated = "customer.created";
    public const string CustomerUpdated = "customer.updated";
    public const string CustomerDeleted = "customer.deleted";
    public const string TestDriveScheduled = "testdrive.scheduled";
}
