namespace CustomerService.DTOs
{
    /// <summary>
    /// Event published by VehicleService to the "vehicle_events" topic exchange
    /// with routing key "vehicle.reserved". Field names must stay in sync with
    /// VehicleService.DTOs.VehicleReservedEvent - see docs/EVENTS.md.
    /// </summary>
    public class VehicleReservedEvent
    {
        public int VehicleId { get; set; }
        public string VehicleName { get; set; } = string.Empty;
        public decimal VehiclePrice { get; set; }
        public int DealerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public int? ColorVariantId { get; set; }
        public string? ColorVariantName { get; set; }
        public int Quantity { get; set; }
        public string? Notes { get; set; }
        public DateTime ReservedAt { get; set; }
    }
}
