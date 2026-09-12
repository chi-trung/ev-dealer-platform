namespace NotificationService.DTOs;

/// <summary>
/// Mirrors CustomerService.DTOs.TestDriveScheduledEvent (vehicle_events /
/// "testdrive.scheduled") - see docs/EVENTS.md. VehicleModel may be empty when
/// CustomerService cannot resolve the vehicle name.
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
