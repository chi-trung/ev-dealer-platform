using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent; // GeneratePdf() extension for QuotePdfDocument (Issue #49)
using SalesService.DTOs;
using SalesService.Models;
using SalesService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SalesService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SalesController : ControllerBase
    {
        private readonly SalesDbContext _context;
        private readonly ILogger<SalesController> _logger;

        public SalesController(SalesDbContext context, ILogger<SalesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private DateTime GetVietnamNow()
        {
            try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")); }
            catch { return DateTime.UtcNow; }
        }

        // --- QUOTE ENDPOINTS ---
        // This CreateQuote method is being commented out because QuotesController is now handling /api/Quotes
        // and frontend is calling /api/Quotes. This avoids duplicate endpoints and build errors.
        /*
        [HttpPost("quotes")]
        public async Task<IActionResult> CreateQuote([FromBody] CreateQuoteDto dto)
        {
            decimal basePrice = 0m;
            try
            {
                using var httpClient = new HttpClient();
                var vehicleApiUrl = $"http://localhost:5003/api/vehicles/{dto.VehicleId}"; // Corrected port to 5003
                var resp = await httpClient.GetAsync(vehicleApiUrl);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("price", out var priceProp) && priceProp.TryGetDecimal(out var price))
                    {
                        basePrice = price;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching vehicle price for VehicleId {VehicleId}", dto.VehicleId);
            }

            if (basePrice <= 0) return BadRequest("Could not determine a valid price for the vehicle.");

            var quote = new Quote
            {
                CustomerId = dto.CustomerId,
                DealerId = dto.DealerId,
                SalespersonId = dto.SalespersonId,
                VehicleId = dto.VehicleId,
                VehicleVariantId = dto.ColorId, // Fixed: Mapped to dto.ColorId as VehicleVariantId is not in DTO
                ColorId = dto.ColorId,
                Quantity = dto.Quantity,
                BasePrice = basePrice,
                TotalBasePrice = basePrice * dto.Quantity,
                Status = "Active",
                CreatedAt = GetVietnamNow(),
                UpdatedAt = GetVietnamNow()
            };

            _context.Quotes.Add(quote);
            await _context.SaveChangesAsync();

            var quoteDto = new QuoteDto { // Mapping properties manually for now
                Id = quote.Id,
                CustomerId = quote.CustomerId,
                DealerId = quote.DealerId,
                SalespersonId = quote.SalespersonId,
                VehicleId = quote.VehicleId,
                VehicleVariantId = quote.VehicleVariantId,
                ColorId = quote.ColorId,
                Quantity = quote.Quantity,
                BasePrice = quote.BasePrice,
                TotalBasePrice = quote.TotalBasePrice,
                Status = quote.Status,
                CreatedAt = quote.CreatedAt,
                UpdatedAt = quote.UpdatedAt
            }; 
            return CreatedAtAction(nameof(GetQuoteById), new { id = quote.Id }, quoteDto);
        }
        */

        [HttpGet("quotes/{id}")]
        public async Task<ActionResult<QuoteDto>> GetQuoteById(int id)
        {
            var quote = await _context.Quotes.FindAsync(id);
            if (quote == null) return NotFound();
            // Map entity to DTO
            var dto = new QuoteDto { // Mapping properties manually for now
                Id = quote.Id,
                CustomerId = quote.CustomerId,
                DealerId = quote.DealerId,
                SalespersonId = quote.SalespersonId,
                VehicleId = quote.VehicleId,
                VehicleVariantId = quote.VehicleVariantId,
                ColorId = quote.ColorId,
                Quantity = quote.Quantity,
                BasePrice = quote.BasePrice,
                TotalBasePrice = quote.TotalBasePrice,
                Status = quote.Status,
                CreatedAt = quote.CreatedAt,
                UpdatedAt = quote.UpdatedAt
            };
            return Ok(dto);
        }
        
        // --- ORDER ENDPOINTS ---

        [HttpPost("orders")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto)
        {
            var quote = await _context.Quotes.FindAsync(dto.QuoteId);
            if (quote == null || quote.Status != "Active")
            {
                return BadRequest("Quote not found or is not active.");
            }

            // Calculate totals
            var subTotal = dto.UnitPrice * dto.Quantity;
            var totalDiscount = dto.DiscountAmount ?? (subTotal * (dto.DiscountPercent ?? 0) / 100);
            var totalPrice = subTotal - totalDiscount;

            var order = new Order
            {
                QuoteId = dto.QuoteId,
                CustomerId = dto.CustomerId,
                DealerId = dto.DealerId,
                SalespersonId = dto.SalespersonId,
                OrderNumber = $"OR-{GetVietnamNow():yyyy}-{new Random().Next(100000, 999999)}",
                VehicleId = dto.VehicleId,
                VariantId = dto.VariantId,
                ColorId = dto.ColorId,
                Quantity = dto.Quantity,
                UnitPrice = dto.UnitPrice,
                DiscountPercent = dto.DiscountPercent,
                DiscountAmount = totalDiscount, // Use calculated totalDiscount
                SubTotal = subTotal,
                TotalDiscount = totalDiscount,
                TotalPrice = totalPrice,
                PaymentMethod = dto.PaymentMethod,
                PaymentForm = dto.PaymentForm,
                DeliveryPreferredDate = dto.DeliveryPreferredDate,
                DeliveryExpectedDate = dto.DeliveryExpectedDate,
                DepositAmount = dto.DepositAmount,
                LoanTermMonths = dto.LoanTermMonths,
                InterestRateYearly = dto.InterestRateYearly,
                Status = "Pending",
                Notes = dto.Notes,
                CreatedAt = GetVietnamNow(), // Use GetVietnamNow()
                UpdatedAt = GetVietnamNow() // Use GetVietnamNow()
            };

            _context.Orders.Add(order);
            
            quote.Status = "ConvertedToOrder";
            quote.UpdatedAt = GetVietnamNow(); // Use GetVietnamNow()

            await _context.SaveChangesAsync();

            var orderDto = new OrderDto { // Mapping properties manually for now
                OrderId = order.OrderId,
                QuoteId = order.QuoteId,
                CustomerId = order.CustomerId,
                DealerId = order.DealerId,
                SalespersonId = order.SalespersonId,
                OrderNumber = order.OrderNumber,
                VehicleId = order.VehicleId,
                VariantId = order.VariantId,
                ColorId = order.ColorId,
                Quantity = order.Quantity,
                UnitPrice = order.UnitPrice,
                DiscountPercent = order.DiscountPercent,
                DiscountAmount = order.DiscountAmount,
                SubTotal = order.SubTotal,
                TotalDiscount = order.TotalDiscount,
                TotalPrice = order.TotalPrice,
                PaymentMethod = order.PaymentMethod,
                PaymentForm = order.PaymentForm,
                DeliveryPreferredDate = order.DeliveryPreferredDate,
                DeliveryExpectedDate = order.DeliveryExpectedDate,
                DepositAmount = order.DepositAmount,
                LoanTermMonths = order.LoanTermMonths,
                InterestRateYearly = order.InterestRateYearly,
                Status = order.Status,
                Notes = order.Notes,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt
            };
            return CreatedAtAction(nameof(GetOrderById), new { id = order.OrderId }, orderDto);
        }

        [HttpGet("orders/{id}")]
        public async Task<ActionResult<OrderDto>> GetOrderById(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null) return NotFound();
            // Map entity to DTO
            var dto = new OrderDto { // Mapping properties manually for now
                OrderId = order.OrderId,
                QuoteId = order.QuoteId,
                CustomerId = order.CustomerId,
                DealerId = order.DealerId,
                SalespersonId = order.SalespersonId,
                OrderNumber = order.OrderNumber,
                VehicleId = order.VehicleId,
                VariantId = order.VariantId,
                ColorId = order.ColorId,
                Quantity = order.Quantity,
                UnitPrice = order.UnitPrice,
                DiscountPercent = order.DiscountPercent,
                DiscountAmount = order.DiscountAmount,
                SubTotal = order.SubTotal,
                TotalDiscount = order.TotalDiscount,
                TotalPrice = order.TotalPrice,
                PaymentMethod = order.PaymentMethod,
                PaymentForm = order.PaymentForm,
                DeliveryPreferredDate = order.DeliveryPreferredDate,
                DeliveryExpectedDate = order.DeliveryExpectedDate,
                DepositAmount = order.DepositAmount,
                LoanTermMonths = order.LoanTermMonths,
                InterestRateYearly = order.InterestRateYearly,
                Status = order.Status,
                Notes = order.Notes,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt
            };
            return Ok(dto);
        }

        // --- CONTRACT ENDPOINTS ---

        [HttpPost("contracts")]
        public async Task<IActionResult> CreateContract([FromBody] CreateContractDto dto)
        {
            var order = await _context.Orders.FindAsync(dto.OrderId);
            if (order == null) return BadRequest("Order not found.");

            var contract = new Contract
            {
                OrderId = dto.OrderId,
                CustomerId = dto.CustomerId,
                DealerId = dto.DealerId,
                SalespersonId = dto.SalespersonId,
                ContractNumber = $"CT-{GetVietnamNow():yyyyMMdd}-{new Random().Next(1000, 9999)}",
                SignedDate = DateOnly.FromDateTime(GetVietnamNow()),
                TotalAmount = dto.TotalAmount,
                PaymentStatus = "Unpaid",
                Status = "PendingApproval",
                Notes = dto.Notes,
                CreatedAt = GetVietnamNow(), // Use GetVietnamNow()
                UpdatedAt = GetVietnamNow() // Use GetVietnamNow()
            };

            _context.Contracts.Add(contract);
            
            order.Status = "ContractRequired";
            order.UpdatedAt = GetVietnamNow(); // Use GetVietnamNow()

            await _context.SaveChangesAsync();
            
            var contractDto = new ContractDto { // Mapping properties manually for now
                ContractId = contract.ContractId,
                OrderId = contract.OrderId,
                CustomerId = contract.CustomerId,
                DealerId = contract.DealerId,
                SalespersonId = contract.SalespersonId,
                ContractNumber = contract.ContractNumber,
                SignedDate = contract.SignedDate,
                TotalAmount = contract.TotalAmount,
                PaymentStatus = contract.PaymentStatus,
                Status = contract.Status,
                Notes = contract.Notes,
                CreatedAt = contract.CreatedAt,
                UpdatedAt = contract.UpdatedAt
            };
            return CreatedAtAction(nameof(GetContractById), new { id = contract.ContractId }, contractDto);
        }

        [HttpGet("contracts/{id}")]
        public async Task<ActionResult<ContractDto>> GetContractById(int id)
        {
            var contract = await _context.Contracts.FindAsync(id);
            if (contract == null) return NotFound();
            var dto = new ContractDto { // Mapping properties manually for now
                ContractId = contract.ContractId,
                OrderId = contract.OrderId,
                CustomerId = contract.CustomerId,
                DealerId = contract.DealerId,
                SalespersonId = contract.SalespersonId,
                ContractNumber = contract.ContractNumber,
                SignedDate = contract.SignedDate,
                TotalAmount = contract.TotalAmount,
                PaymentStatus = contract.PaymentStatus,
                Status = contract.Status,
                Notes = contract.Notes,
                CreatedAt = contract.CreatedAt,
                UpdatedAt = contract.UpdatedAt
            };
            return Ok(dto);
        }

        /// <summary>
        /// Issue #49: render a quote as PDF server-side. The frontend "Tải PDF"
        /// buttons (QuoteView/QuoteCreate) have called this route since before
        /// the gateway epic, but SalesService never shipped it — QuotePdfDocument
        /// and the DTO existed orphaned. Stateless: the payload carries the
        /// fully-computed quote (customer/items/totals the browser already
        /// assembled), so no DB read is involved and auth posture matches the
        /// sibling anonymous quote/order/contract endpoints. Returns raw PDF
        /// bytes with application/pdf so the blob download works unchanged.
        /// </summary>
        [HttpPost("generate-quote-pdf")]
        public IActionResult GenerateQuotePdf([FromBody] GenerateQuotePdfRequestDto dto)
        {
            if (dto is null) return BadRequest("A quote payload is required.");
            try
            {
                // License is set at process start in Program.cs with fail-soft;
                // if it never initialized (headless Docker edge), generating now
                // would throw — surface that as 503, not a 500 stack trace.
                var bytes = new PdfDocuments.QuotePdfDocument(dto).GeneratePdf();
                return File(bytes, "application/pdf", "BaoGiaXeDien.pdf");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "PDF generation unavailable (QuestPDF not initialized)");
                return StatusCode(503, "PDF generation is not available right now.");
            }
        }
    }
}
