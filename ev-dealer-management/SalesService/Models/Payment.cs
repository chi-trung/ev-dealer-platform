using System;
using System.ComponentModel.DataAnnotations; // Required for [Key] if not already present
using System.ComponentModel.DataAnnotations.Schema; // Required for [Column] if not already present

namespace SalesService.Models
{
    public class Payment
    {
        // Helper method to get Vietnam local time
        private static DateTime GetVietnamNow()
        {
            try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")); }
            catch { return DateTime.UtcNow; }
        }

        public Guid PaymentId { get; set; }

        // FK — int, matching Order's int PK and the Contract pattern. This was
        // Guid until Issue #37: the mismatch made the link a shadow-property
        // afterthought (OrderId1) and left PaymentReceivedEvent with no way to
        // resolve the customer for a push.
        public int OrderId { get; set; }
        public Order? Order { get; set; }

        public decimal Amount { get; set; }
        public string Method { get; set; } = string.Empty; // Cash, BankTransfer, Card
        public string Status { get; set; } = "Pending";    // Pending, Paid, Failed

        // New field
        public string? TransactionCode { get; set; }

        public DateTime? PaidDate { get; set; }

        public string? Notes { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = GetVietnamNow(); // Use GetVietnamNow()
        public DateTime UpdatedAt { get; set; } = GetVietnamNow(); // Use GetVietnamNow()
    }
}
