using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesService.Data;
using SalesService.Models; // Ensure Quote model is imported
using Microsoft.Extensions.Logging;
using SalesService.DTOs; // Import DTOs namespace
using SalesService.Services;
using Microsoft.Extensions.Configuration;
using System; // Required for DateTime

using Microsoft.AspNetCore.Authorization;
namespace SalesService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Issue #137: quotes carry customer identity, vehicle pricing and discount
    // amounts -- the whole controller is the sales workflow.
    [Authorize]
    public class QuotesController : ControllerBase
    {
        private readonly SalesDbContext _context;
        private readonly ILogger<QuotesController> _logger;
        private readonly IMessagePublisher _messagePublisher;
        private readonly IConfiguration _configuration;

        public QuotesController(
            SalesDbContext context, 
            ILogger<QuotesController> logger,
            IMessagePublisher messagePublisher,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _messagePublisher = messagePublisher;
            _configuration = configuration;
        }

        private DateTime GetVietnamNow()
        {
            try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")); }
            catch { return DateTime.UtcNow; }
        }

        /// <summary>
        /// Get all quotes.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Quote>>> GetAllQuotes()
        {
            try
            {
                var quotes = await _context.Quotes.ToListAsync();
                _logger.LogInformation("Retrieved {Count} quotes.", quotes.Count);
                return Ok(quotes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all quotes.");
                return StatusCode(500, new { message = "Failed to retrieve quotes", error = ex.Message });
            }
        }

        /// <summary>
        /// Get a quote by its ID.
        /// </summary>
        [HttpGet("{id}")] // Route for getting a single quote by ID
        public async Task<ActionResult<QuoteDto>> GetQuoteById(int id)
        {
            try
            {
                var quote = await _context.Quotes.FindAsync(id);

                if (quote == null)
                {
                    _logger.LogWarning("Quote with ID {QuoteId} not found.", id);
                    return NotFound();
                }

                // Map the Quote entity to a QuoteDto
                var quoteDto = new QuoteDto
                {
                    Id = quote.Id,
                    CustomerId = quote.CustomerId,
                    DealerId = quote.DealerId,
                    SalespersonId = quote.SalespersonId,
                    VehicleId = quote.VehicleId,
                    VehicleVariantId = quote.VehicleVariantId, // Assuming this is still needed or maps to ColorId
                    ColorId = quote.ColorId,
                    Quantity = quote.Quantity,
                    BasePrice = quote.BasePrice,
                    TotalBasePrice = quote.TotalBasePrice,
                    Status = quote.Status,
                    Notes = quote.Notes, // Include Notes
                    CreatedAt = quote.CreatedAt,
                    UpdatedAt = quote.UpdatedAt
                };

                _logger.LogInformation("Retrieved quote with ID: {QuoteId}", id);
                return Ok(quoteDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving quote with ID {QuoteId}.", id);
                return StatusCode(500, new { message = "Failed to retrieve quote", error = ex.Message });
            }
        }

        /// <summary>
        /// Create a new quote.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<Quote>> CreateQuote([FromBody] CreateQuoteDto createQuoteDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var quote = new Quote
                {
                    CustomerId = createQuoteDto.CustomerId,
                    DealerId = createQuoteDto.DealerId,
                    SalespersonId = createQuoteDto.SalespersonId,
                    VehicleId = createQuoteDto.VehicleId,
                    // Assuming VehicleVariantId maps to ColorId in the current context
                    VehicleVariantId = createQuoteDto.ColorId, 
                    ColorId = createQuoteDto.ColorId, // Keep both if needed, or clarify mapping
                    Quantity = createQuoteDto.Quantity,
                    BasePrice = createQuoteDto.UnitPrice, // Map UnitPrice from DTO to BasePrice in model
                    TotalBasePrice = createQuoteDto.TotalPrice, // Map TotalPrice from DTO to TotalBasePrice in model
                    Status = createQuoteDto.Status,
                    Notes = createQuoteDto.Notes, // Added mapping for Notes
                    CreatedAt = GetVietnamNow(), // Use GetVietnamNow()
                    UpdatedAt = GetVietnamNow() // Use GetVietnamNow()
                };

                _context.Quotes.Add(quote);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Quote created successfully with ID: {QuoteId}", quote.Id);

                // Publish QuoteCreated event to RabbitMQ
                try
                {
                    var quoteCreatedEvent = new QuoteCreatedEvent
                    {
                        QuoteId = quote.Id.ToString(),
                        CustomerId = quote.CustomerId,
                        DealerId = quote.DealerId,
                        SalespersonId = quote.SalespersonId,
                        VehicleId = quote.VehicleId,
                        Quantity = quote.Quantity,
                        TotalBasePrice = quote.TotalBasePrice,
                        Status = quote.Status,
                        CreatedAt = quote.CreatedAt
                    };

                    var quoteCreatedQueue = _configuration["RabbitMQ:Queues:QuoteCreated"] ?? "quote.created";
                    await _messagePublisher.PublishMessageAsync(quoteCreatedQueue, quoteCreatedEvent);
                    _logger.LogInformation("Published QuoteCreated event for Quote {QuoteId}", quote.Id);
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the request
                    _logger.LogError(ex, "Error publishing QuoteCreated event to RabbitMQ for Quote {QuoteId}", quote.Id);
                }

                return CreatedAtAction(nameof(GetAllQuotes), new { id = quote.Id }, quote); // Return 201 Created
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating quote.");
                return StatusCode(500, new { message = "Failed to create quote", error = ex.Message });
            }
        }

        /// <summary>
        /// Update the status of a quote.
        /// </summary>
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateQuoteStatus(int id, [FromBody] UpdateQuoteStatusDto updateQuoteStatusDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var quote = await _context.Quotes.FindAsync(id);

                if (quote == null)
                {
                    _logger.LogWarning("Quote with ID {QuoteId} not found for status update.", id);
                    return NotFound();
                }

                quote.Status = updateQuoteStatusDto.Status;
                quote.UpdatedAt = GetVietnamNow();

                _context.Entry(quote).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Quote ID {QuoteId} status updated to {NewStatus}.", id, updateQuoteStatusDto.Status);
                return NoContent(); // 204 No Content is typical for successful PUT/PATCH updates
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for quote with ID {QuoteId}.", id);
                return StatusCode(500, new { message = "Failed to update quote status", error = ex.Message });
            }
        }
    }
}
