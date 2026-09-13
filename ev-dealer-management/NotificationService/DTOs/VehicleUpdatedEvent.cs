namespace NotificationService.DTOs
{
    /// <summary>
    /// Mirrors VehicleService.DTOs.VehicleUpdatedEvent, published to the
    /// vehicle_events topic exchange with routing key "vehicle.updated".
    /// </summary>
    public class VehicleUpdatedEvent
    {
        public int VehicleId { get; set; }
        public string Model { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int DealerId { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
