using Microsoft.AspNetCore.Mvc;
using SalesService.DTOs;
using SalesService.Services;
using Microsoft.Extensions.Logging;
using SalesService.Data;
using Microsoft.EntityFrameworkCore;
using SalesService.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Text.Json;
using System; // Required for DateTime

namespace SalesService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly ILogger<OrdersController> _logger;
        private readonly SalesDbContext _context;
        private readonly IMessagePublisher _messagePublisher;
        private readonly IConfiguration _configuration;

        public OrdersController(
            ILogger<OrdersController> logger, 
            SalesDbContext context,
            IMessagePublisher messagePublisher,
            IConfiguration configuration)
        {
            _logger = logger;
            _context = context;
            _messagePublisher = messagePublisher;
            _configuration = configuration;
        }

        /// <summary>
        /// Create a new order and send order confirmation email
        /// </summary>
        [HttpPost("complete")]
        public async Task<IActionResult> CompleteOrder([FromBody] CreateOrderRequest request)
        {
            try
            {
                _logger.LogInformation("Received CreateOrderRequest: {@Request}", request); // Log the entire request object

                // Basic validation from DTO
                if (string.IsNullOrWhiteSpace(request.CustomerEmail))
                {
                    _logger.LogWarning("Customer email is missing in CreateOrderRequest.");
                    return BadRequest(new { message = "Customer email is required" });
                }

                if (string.IsNullOrWhiteSpace(request.CustomerName))
                {
                    _logger.LogWarning("Customer name is missing in CreateOrderRequest.");
                    return BadRequest(new { message = "Customer name is required" });
                }

                // --- CẬP NHẬT TRẠNG THÁI QUOTE TỪ ACTIVE SANG CONVERTEDTOORDER ---
                // Kiểm tra nếu QuoteId > 0 (hợp lệ)
                if (request.QuoteId > 0)
                {
                    var quote = await _context.Quotes.FindAsync(request.QuoteId);
                    if (quote != null)
                    {
                        if (quote.Status == "Active")
                        {
                            quote.Status = "ConvertedToOrder";
                            quote.UpdatedAt = DateTime.UtcNow;
                            _logger.LogInformation("Updated quote {QuoteId} status from Active to ConvertedToOrder", request.QuoteId);
                        }
                        else
                        {
                            _logger.LogWarning("Quote {QuoteId} status is {CurrentStatus}, cannot convert to order", request.QuoteId, quote.Status);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Quote with ID {QuoteId} not found", request.QuoteId);
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid QuoteId: {QuoteId}", request.QuoteId);
                }
                
                // Create new Order object
                var order = new Order
                {
                    QuoteId = request.QuoteId,
                    CustomerId = request.CustomerId,
                    DealerId = request.DealerId,
                    SalespersonId = request.SalespersonId,
                    VehicleId = request.VehicleId,
                    VariantId = request.VehicleVariantId, // Map from VehicleVariantId in DTO
                    ColorId = request.ColorId,
                    Quantity = request.Quantity,
                    UnitPrice = request.UnitPrice,
                    
                    // Payment & Delivery - Đã sửa lỗi ánh xạ ở đây
                    PaymentMethod = request.PaymentMethod ?? "Unknown", // Sửa: Lấy từ request.PaymentMethod
                    PaymentForm = request.PaymentType ?? "Unknown",    // Sửa: Lấy từ request.PaymentType
                    DeliveryPreferredDate = request.DeliveryDate,
                    DeliveryExpectedDate = request.EstimatedDeliveryDate,
                    Notes = request.Notes,

                    // Installment Info (Nullable)
                    DepositAmount = request.DepositAmount,
                    LoanTermMonths = request.LoanTermMonths,
                    InterestRateYearly = request.InterestRateYearly,

                    // Promotion fields
                    DiscountPercent = request.DiscountPercent,
                    DiscountAmount = request.DiscountAmount,

                    // Initialize required properties with default values
                    OrderNumber = "", // Will be set below
                    Status = "Pending" // Default status
                };

                _logger.LogInformation("Order object initialized from request (before price calculation): UnitPrice={UnitPrice}, Quantity={Quantity}, DiscountAmount={DiscountAmount}, DiscountPercent={DiscountPercent}",
                    order.UnitPrice, order.Quantity, order.DiscountAmount, order.DiscountPercent);

                // --- Calculate SubTotal, TotalDiscount, TotalPrice on Backend ---
                order.SubTotal = order.UnitPrice * order.Quantity;

                decimal totalDiscountCalculated = 0;
                if (order.DiscountAmount.HasValue)
                {
                    totalDiscountCalculated = order.DiscountAmount.Value;
                }
                else if (order.DiscountPercent.HasValue)
                {
                    totalDiscountCalculated = order.SubTotal * (order.DiscountPercent.Value / 100);
                }
                order.TotalDiscount = totalDiscountCalculated;

                order.TotalPrice = order.SubTotal - order.TotalDiscount;

                // Ensure TotalPrice is not negative
                if (order.TotalPrice < 0)
                {
                    order.TotalPrice = 0;
                }

                _logger.LogInformation("Order price calculation results: SubTotal={SubTotal}, TotalDiscount={TotalDiscount}, TotalPrice={TotalPrice}",
                    order.SubTotal, order.TotalDiscount, order.TotalPrice);

                // --- Backend Validation for TotalPrice ---
                if (order.TotalPrice <= 0)
                {
                    _logger.LogWarning("Order TotalPrice is <= 0 ({TotalPrice}). Returning BadRequest.", order.TotalPrice);
                    return BadRequest(new { message = "Total price calculated on backend must be greater than 0. Please check quote details or promotion." });
                }

                // Generate OrderNumber and set Status
                order.OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";
                order.Status = "Pending"; // Default status for a new order

                // Add to database and save changes (bao gồm cả cập nhật quote status)
                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Order {OrderNumber} completed for customer {CustomerEmail}. Order ID: {OrderId}. Quote status updated to ConvertedToOrder.", 
                    order.OrderNumber, request.CustomerEmail, order.OrderId);

                // Publish events to RabbitMQ
                _logger.LogInformation("Starting to publish events to RabbitMQ for Order {OrderNumber}", order.OrderNumber);
                try
                {
                    // Get vehicle model name
                    _logger.LogInformation("Fetching vehicle model for VehicleId {VehicleId}", order.VehicleId);
                    var vehicleModel = await GetVehicleModelAsync(order.VehicleId);
                    _logger.LogInformation("Vehicle model retrieved: {VehicleModel}", vehicleModel);

                    // Publish OrderCreated event
                    var orderCreatedEvent = new OrderCreatedEvent
                    {
                        OrderId = order.OrderId.ToString(),
                        OrderNumber = order.OrderNumber,
                        CustomerId = order.CustomerId,
                        DealerId = order.DealerId,
                        VehicleId = order.VehicleId,
                        Quantity = order.Quantity,
                        TotalPrice = order.TotalPrice,
                        PaymentMethod = order.PaymentMethod,
                        Status = order.Status,
                        CreatedAt = order.CreatedAt
                    };

                    var orderCreatedQueue = _configuration["RabbitMQ:Queues:OrderCreated"] ?? "order.created";
                    await _messagePublisher.PublishMessageAsync(orderCreatedQueue, orderCreatedEvent);
                    _logger.LogInformation("Published OrderCreated event for Order {OrderNumber}", order.OrderNumber);

                    // Publish SaleCompleted event (for NotificationService)
                    var saleCompletedEvent = new SaleCompletedEvent
                    {
                        OrderId = order.OrderId.ToString(),
                        CustomerEmail = request.CustomerEmail,
                        CustomerName = request.CustomerName,
                        VehicleModel = vehicleModel,
                        TotalPrice = order.TotalPrice,
                        CompletedAt = order.CreatedAt,
                        DeviceToken = null, // Can be added later if available in request
                        CustomerId = order.CustomerId // Issue #37: registry fallback key
                    };

                    var saleCompletedQueue = _configuration["RabbitMQ:Queues:SaleCompleted"] ?? "sales.completed";
                    await _messagePublisher.PublishMessageAsync(saleCompletedQueue, saleCompletedEvent);
                    _logger.LogInformation("Published SaleCompleted event for Order {OrderNumber}", order.OrderNumber);
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the request
                    _logger.LogError(ex, "Error publishing events to RabbitMQ for Order {OrderNumber}", order.OrderNumber);
                }

                return Ok(new
                {
                    success = true,
                    message = "Order completed successfully and quote status updated to ConvertedToOrder.",
                    orderId = order.OrderId,
                    orderNumber = order.OrderNumber,
                    customerEmail = request.CustomerEmail,
                    totalPrice = order.TotalPrice,
                    quoteStatusUpdated = request.QuoteId > 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing order");
                return StatusCode(500, new { message = "Failed to complete order", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all orders.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Order>>> GetAllOrders()
        {
            try
            {
                // Include the related Contract entity
                var orders = await _context.Orders
                                           .Include(o => o.Contract)
                                           .ToListAsync();
                _logger.LogInformation("Retrieved {Count} orders with their contracts.", orders.Count);
                return Ok(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all orders from database.");
                return StatusCode(500, new { message = "Failed to retrieve orders from database", error = ex.Message });
            }
        }

        /// <summary>
        /// Get a specific order by its ID.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<Order>> GetOrderById(int id)
        {
            try
            {
                // Also include the contract for the single order view
                var order = await _context.Orders
                                          .Include(o => o.Contract)
                                          .FirstOrDefaultAsync(o => o.OrderId == id);

                if (order == null)
                {
                    _logger.LogWarning("Order with ID {OrderId} not found.", id);
                    return NotFound();
                }

                _logger.LogInformation("Retrieved order with ID {OrderId}.", id);
                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order with ID {OrderId}.", id);
                return StatusCode(500, new { message = "Failed to retrieve order", error = ex.Message });
            }
        }

        /// <summary>
        /// Updates the status of an order.
        /// </summary>
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateStatusRequest request)
        {
            _logger.LogInformation("Attempting to update status for Order ID: {OrderId} to {Status}", id, request.Status);

            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                _logger.LogWarning("Order with ID {OrderId} not found.", id);
                return NotFound(new { message = $"Order with ID {id} not found." });
            }

            var oldStatus = order.Status;
            order.Status = request.Status;
            order.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully updated status for Order ID {OrderId} from {OldStatus} to {NewStatus}.", id, oldStatus, request.Status);

                // Publish OrderStatusChanged event
                try
                {
                    var statusChangedEvent = new OrderStatusChangedEvent
                    {
                        OrderId = order.OrderId.ToString(),
                        OrderNumber = order.OrderNumber,
                        OldStatus = oldStatus,
                        NewStatus = request.Status,
                        ChangedAt = order.UpdatedAt,
                        CustomerId = order.CustomerId // Issue #37: registry lookup key for the push
                    };

                    var statusChangedQueue = _configuration["RabbitMQ:Queues:OrderStatusChanged"] ?? "order.status.changed";
                    await _messagePublisher.PublishMessageAsync(statusChangedQueue, statusChangedEvent);
                    _logger.LogInformation("Published OrderStatusChanged event for Order {OrderNumber}", order.OrderNumber);
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the request
                    _logger.LogError(ex, "Error publishing OrderStatusChanged event for Order {OrderNumber}", order.OrderNumber);
                }
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database update failed when updating status for Order ID {OrderId}.", id);
                return StatusCode(500, new { message = "An error occurred while updating the order status.", error = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { message = "Order status updated successfully." });
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "SalesService", timestamp = DateTime.UtcNow });
        }

        /// <summary>
        /// Helper method to get vehicle model name from VehicleService
        /// </summary>
        private async Task<string> GetVehicleModelAsync(int vehicleId)
        {
            try
            {
                var vehicleServiceUrl = _configuration["Services:VehicleService"] ?? "http://localhost:5001";
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var response = await httpClient.GetAsync($"{vehicleServiceUrl}/api/vehicles/{vehicleId}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    
                    // Try to get model name from various possible property names
                    if (doc.RootElement.TryGetProperty("model", out var modelProp))
                    {
                        return modelProp.GetString() ?? $"Vehicle-{vehicleId}";
                    }
                    if (doc.RootElement.TryGetProperty("name", out var nameProp))
                    {
                        return nameProp.GetString() ?? $"Vehicle-{vehicleId}";
                    }
                    if (doc.RootElement.TryGetProperty("vehicleName", out var vehicleNameProp))
                    {
                        return vehicleNameProp.GetString() ?? $"Vehicle-{vehicleId}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch vehicle model for VehicleId {VehicleId}", vehicleId);
            }
            
            // Return fallback value
            return $"Vehicle-{vehicleId}";
        }
    }
}
