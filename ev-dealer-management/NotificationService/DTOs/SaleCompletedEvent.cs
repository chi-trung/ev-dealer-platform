namespace NotificationService.DTOs;

public class SaleCompletedEvent
{
    public required string OrderId { get; set; }
    public required string CustomerEmail { get; set; }
    public required string CustomerName { get; set; }
    public required string VehicleModel { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime CompletedAt { get; set; }
    public string? DeviceToken { get; set; }
    // Issue #37: mirrors the producer field — the device-token registry
    // subject customer:<CustomerId> is the fallback when DeviceToken is
    // absent. Default 0 (a pre-#37 producer message) finds no tokens and
    // degrades to log-only.
    public int CustomerId { get; set; }
}
