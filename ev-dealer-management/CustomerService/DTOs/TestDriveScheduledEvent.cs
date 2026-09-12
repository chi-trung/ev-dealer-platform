namespace CustomerService.DTOs
{
    /// <summary>
    /// Published to the "vehicle_events" topic exchange, routing key
    /// "testdrive.scheduled". Consumed by NotificationService.
    /// VehicleModel is left empty for now: CustomerService has no local copy of
    /// the vehicle catalog (cross-service lookup is planned for a later phase).
    /// DeviceToken is null until the mobile booking flow provides one.
    /// </summary>
    public class TestDriveScheduledEvent
    {
        public int TestDriveId { get; set; }
        public int CustomerId { get; set; }
        public int VehicleId { get; set; }
        public int DealerId { get; set; }
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string VehicleModel { get; set; } = string.Empty;
        public DateTime ScheduledDate { get; set; }
        public string? DeviceToken { get; set; }
    }
}
