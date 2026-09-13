namespace NotificationService.DTOs
{
    /// <summary>
    /// Mirrors VehicleService.DTOs.VehicleDeletedEvent, published to the
    /// vehicle_events topic exchange with routing key "vehicle.deleted".
    /// </summary>
    public class VehicleDeletedEvent
    {
        public int VehicleId { get; set; }
        // Issue #38: mirrors the producer's new DealerId — the registry
        // subject the push fans out to. A pre-#38 in-flight message without
        // it defaults to 0 → dealer:0 → nothing registered → log-only.
        public int DealerId { get; set; }
        public DateTime DeletedAt { get; set; }
    }
}
