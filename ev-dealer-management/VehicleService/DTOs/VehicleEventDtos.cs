namespace VehicleService.DTOs
{
    public class VehicleCreatedEvent
    {
        public int VehicleId { get; set; }
        public string Model { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int DealerId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class VehicleUpdatedEvent
    {
        public int VehicleId { get; set; }
        public string Model { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int DealerId { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class VehicleDeletedEvent
    {
        public int VehicleId { get; set; }
        // Issue #38: the delete event carried no audience data at all, so no
        // consumer could route a push. The vehicle entity is loaded (and thus
        // its DealerId known) at the publish site in DeleteVehicleAsync.
        public int DealerId { get; set; }
        public DateTime DeletedAt { get; set; }
    }
}
