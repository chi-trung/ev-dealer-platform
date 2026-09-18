using System;
using System.ComponentModel.DataAnnotations; // Required for [Key] if not already present
using System.ComponentModel.DataAnnotations.Schema; // Required for [Column] if not already present

namespace SalesService.Models
{
    public class Promotion
    {
        // Helper method to get Vietnam local time
        private static DateTime GetVietnamNow()
        {
            try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")); }
            catch { return DateTime.UtcNow; }
        }

        public Guid PromotionId { get; set; }

        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }

        // Percent / Amount
        public string Type { get; set; } = "Percent";

        // Giá trị giảm (vd: 10 -> 10%)
        // Same precision reason as Payment.Amount / Vehicle.Price: an untyped
        // decimal becomes scale-less numeric under Postgres and loses cents.
        [Column(TypeName = "decimal(18, 2)")]
        public decimal Value { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        // Audit
        public DateTime CreatedAt { get; set; } = GetVietnamNow(); // Use GetVietnamNow()
        public DateTime UpdatedAt { get; set; } = GetVietnamNow(); // Use GetVietnamNow()
    }
}
