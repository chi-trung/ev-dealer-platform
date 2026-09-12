namespace VehicleService.Events;

/// <summary>
/// Canonical routing keys and the shared topic exchange for vehicle-domain events.
/// Consumers (NotificationService, CustomerService) bind their own queues to this
/// exchange - see docs/EVENTS.md for the full topology.
/// </summary>
public static class EventNames
{
    /// <summary>Topic exchange carrying all vehicle.* and testdrive.* events.</summary>
    public const string VehicleExchange = "vehicle_events";

    public const string VehicleCreated = "vehicle.created";
    public const string VehicleUpdated = "vehicle.updated";
    public const string VehicleDeleted = "vehicle.deleted";
    public const string VehicleReserved = "vehicle.reserved";
}
