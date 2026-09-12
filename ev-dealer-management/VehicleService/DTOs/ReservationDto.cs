using System.ComponentModel.DataAnnotations;

namespace VehicleService.DTOs
{
    public class ReservationRequestDto
    {
        [Required(ErrorMessage = "Customer name is required")]
        public string CustomerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Customer email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string CustomerEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Customer phone is required")]
        [Phone(ErrorMessage = "Invalid phone number")]
        public string CustomerPhone { get; set; } = string.Empty;

        public int? ColorVariantId { get; set; }

        public string? Notes { get; set; }

        [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100")]
        public int Quantity { get; set; } = 1;

        public string? DeviceToken { get; set; }
    }

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
        public string? DeviceToken { get; set; }
    }
}
