namespace NotificationService.DTOs
{
    /// <summary>
    /// Mirrors VehicleService.DTOs.VehicleDeletedEvent, published to the
    /// vehicle_events topic exchange with routing key "vehicle.deleted".
    /// </summary>
    public class VehicleDeletedEvent
    {
        public int VehicleId { get; set; }
        public DateTime DeletedAt { get; set; }
    }
}
