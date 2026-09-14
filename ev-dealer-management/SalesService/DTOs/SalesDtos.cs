using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SalesService.DTOs
{
    // --- DTOs for QUOTE (Refactored) ---
    public class CreateQuoteDto
    {
        [Required] public int CustomerId { get; set; }
        [Required] public int DealerId { get; set; }
        [Required] public int SalespersonId { get; set; }
        [Required] public int VehicleId { get; set; }
        // Frontend sends colorVariantId, which maps to ColorId in Quote model
        [Required] public int ColorId { get; set; } 
        [Range(1, 100)] public int Quantity { get; set; } = 1;
        
        // Add pricing and status fields from frontend payload that map to Quote model
        [Required] public decimal UnitPrice { get; set; } // Maps to BasePrice in Quote model
        [Required] public decimal TotalPrice { get; set; } // Maps to TotalBasePrice in Quote model
        [Required, MaxLength(50)] public string Status { get; set; } = "Active"; // Match Quote model status
        
        // Added Notes field
        [MaxLength(1000)] public string? Notes { get; set; }

        // Frontend also sends salespersonName, paymentType, downPaymentPercent, loanTerm, interestRate, quoteItems.
        // These are not directly in the current flat Quote model.
        // For now, we'll only include what maps directly to the Quote model.
    }

    public class QuoteDto
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int DealerId { get; set; }
        public int SalespersonId { get; set; }
        public int VehicleId { get; set; }
        public int VehicleVariantId { get; set; } // This should probably be ColorId to match Quote model
        public int ColorId { get; set; }
        public int Quantity { get; set; }
        public decimal BasePrice { get; set; }
        public decimal TotalBasePrice { get; set; }
        public string Status { get; set; } = "Active";
        public string? Notes { get; set; } // Added Notes to QuoteDto for retrieval
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class UpdateQuoteStatusDto
    {
        [Required, MaxLength(50)]
        public string Status { get; set; } = string.Empty;
    }

    // --- DTOs for ORDER (Refactored) ---
    public class CreateOrderDto
    {
        [Required] public int QuoteId { get; set; }
        [Required] public int CustomerId { get; set; }
        [Required] public int DealerId { get; set; }
        [Required] public int SalespersonId { get; set; }
        [Required] public int VehicleId { get; set; }
        [Required] public int VariantId { get; set; }
        [Required] public int ColorId { get; set; }
        [Range(1, 100)] public int Quantity { get; set; } = 1;
        [Required] public decimal UnitPrice { get; set; }
        public decimal? DiscountPercent { get; set; }
        public decimal? DiscountAmount { get; set; }
        [Required] public string PaymentMethod { get; set; } = string.Empty;
        [Required] public string PaymentForm { get; set; } = string.Empty;
        [Required] public DateTime DeliveryPreferredDate { get; set; }
        [Required] public DateTime DeliveryExpectedDate { get; set; }
        public string? DeliveryAddress { get; set; } // Added DeliveryAddress
        public decimal? DepositAmount { get; set; }
        public int? LoanTermMonths { get; set; }
        public decimal? InterestRateYearly { get; set; }
        public string? Notes { get; set; }
    }

    public class OrderDto
    {
        public int OrderId { get; set; }
        public int QuoteId { get; set; }
        public int CustomerId { get; set; }
        public int DealerId { get; set; }
        public int SalespersonId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public int VehicleId { get; set; }
        public int VariantId { get; set; }
        public int ColorId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal? DiscountPercent { get; set; }
        public decimal? DiscountAmount { get; set; }
        public decimal SubTotal { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal TotalPrice { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentForm { get; set; } = string.Empty;
        public DateTime DeliveryPreferredDate { get; set; }
        public DateTime DeliveryExpectedDate { get; set; }
        public string? DeliveryAddress { get; set; } // Added DeliveryAddress
        public decimal? DepositAmount { get; set; }
        public int? LoanTermMonths { get; set; }
        public decimal? InterestRateYearly { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // --- DTOs for CONTRACT (Refactored) ---
    public class CreateContractDto
    {
        [Required] public int OrderId { get; set; }
        [Required] public int CustomerId { get; set; }
        [Required] public int DealerId { get; set; }
        [Required] public int SalespersonId { get; set; }
        [Required] public decimal TotalAmount { get; set; }
        public string? Notes { get; set; }
    }

    public class ContractDto
    {
        public int ContractId { get; set; }
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public int DealerId { get; set; }
        public int SalespersonId { get; set; }
        public string ContractNumber { get; set; } = string.Empty;
        public DateOnly SignedDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // --- DTOs for OTHER SERVICES (Restored & Corrected) ---
    public class CreateDeliveryDto {
        [Required] public Guid OrderId { get; set; }
        [Required, StringLength(100)] public string TrackingNumber { get; set; } = string.Empty;
        [Required] public DateTime EstimatedDeliveryDate { get; set; }
        public DateTime? ActualDeliveryDate { get; set; }
        [Required, StringLength(50)] public string Status { get; set; } = "Pending";
        [StringLength(1000)] public string? Notes { get; set; }
    }
    public class UpdateDeliveryStatusDto {
        [Required, StringLength(50)] public string Status { get; set; } = string.Empty;
        public DateTime? ActualDeliveryDate { get; set; }
        [StringLength(1000)] public string? Notes { get; set; }
    }
    public class DeliveryDto {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public string TrackingNumber { get; set; } = string.Empty;
        public DateTime EstimatedDeliveryDate { get; set; }
        public DateTime? ActualDeliveryDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
    public class CreatePaymentDto {
        // Issue #37: int, matching Order's int PK (was Guid — the type mismatch
        // made the Payments→Orders link a shadow-column afterthought).
        [Required] public int OrderId { get; set; }
        [Required, Range(0.01, 10000000000.00)] public decimal Amount { get; set; }
        [Required] public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        [Required, StringLength(50)] public string PaymentMethod { get; set; } = "Cash";
        [Required, StringLength(50)] public string Status { get; set; } = "Completed";
        [StringLength(200)] public string? TransactionId { get; set; }
        [StringLength(1000)] public string? Notes { get; set; }
    }
    public class UpdatePaymentDto {
        [Range(0.01, 10000000000.00)] public decimal? Amount { get; set; }
        [StringLength(50)] public string? PaymentMethod { get; set; }
        [StringLength(50)] public string? Status { get; set; }
        [StringLength(200)] public string? TransactionId { get; set; }
        [StringLength(1000)] public string? Notes { get; set; }
    }
    public class PaymentDto {
        public Guid Id { get; set; }
        public int OrderId { get; set; } // Issue #37: was Guid (see CreatePaymentDto)
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? TransactionId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
    public class CreatePromotionDto {
        [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
        [StringLength(1000)] public string? Description { get; set; }
        [Required] public DateTime StartDate { get; set; }
        [Required] public DateTime EndDate { get; set; }
        [Required, Range(0.01, 1000000000.00)] public decimal DiscountValue { get; set; }
        [Required, StringLength(50)] public string DiscountType { get; set; } = "Percentage";
        [StringLength(50)] public string? ApplicableTo { get; set; }
        public int? VehicleId { get; set; }
    }
    public class UpdatePromotionDto {
        [StringLength(200)] public string? Name { get; set; }
        [StringLength(1000)] public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        [Range(0.01, 1000000000.00)] public decimal? DiscountValue { get; set; }
        [StringLength(50)] public string? DiscountType { get; set; }
        [StringLength(50)] public string? ApplicableTo { get; set; }
        public int? VehicleId { get; set; }
        public bool? IsActive { get; set; }
    }
    public class PromotionDto {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal DiscountValue { get; set; }
        public string DiscountType { get; set; } = string.Empty;
        public string? ApplicableTo { get; set; }
        public int? VehicleId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
    // Issue #49: values are `object` (deserialized as JsonElement) because the
    // frontend sends numbers inside these dicts (vehicleId, quantity, unitPrice,
    // discountPercent, paymentInfo percentages). Dictionary<string,string> made
    // System.Text.Json 400 every real payload — the route could never work even
    // if it had existed. QuotePdfDocument renders values via .ToString().
    public class GenerateQuotePdfRequestDto
    {
        public Dictionary<string, object?> CustomerInfo { get; set; } = new();
        public List<Dictionary<string, object?>> QuoteItems { get; set; } = new();
        public Dictionary<string, object?> PaymentInfo { get; set; } = new();
        public Dictionary<string, object?> AdditionalInfo { get; set; } = new();
        // The frontend computes these as raw JS numbers (QuoteView calculateTotals),
        // so they're widened to object? alongside the dicts for the same reason.
        public object? TotalCalculatedAmount { get; set; }
        public object? DownPaymentCalculated { get; set; }
        public object? LoanAmountCalculated { get; set; }
        public object? MonthlyPaymentCalculated { get; set; }
        public object? InstallmentTotalPaymentCalculated { get; set; }
    }
}
