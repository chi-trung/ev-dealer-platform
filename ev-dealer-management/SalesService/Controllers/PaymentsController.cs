using Microsoft.AspNetCore.Mvc;
using SalesService.Data;
using SalesService.DTOs;
using SalesService.Models;
using SalesService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
namespace SalesService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Issue #137: payment records are financial data; both the list and the
    // recording of a payment are staff actions.
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly SalesDbContext _context;
        private readonly IMessagePublisher _messagePublisher;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            SalesDbContext context,
            IMessagePublisher messagePublisher,
            IConfiguration configuration,
            ILogger<PaymentsController> logger)
        {
            _context = context;
            _messagePublisher = messagePublisher;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PaymentDto>>> GetPayments()
        {
            var payments = await _context.Payments
                .Select(p => new PaymentDto
                {
                    Id = p.PaymentId,
                    OrderId = p.OrderId,
                    Amount = p.Amount,
                    PaymentDate = p.PaidDate ?? DateTime.MinValue,
                    PaymentMethod = p.Method,
                    Status = p.Status,
                    TransactionId = p.TransactionCode,
                    Notes = p.Notes,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                })
                .ToListAsync();
            return Ok(payments);
        }

        [HttpPost]
        public async Task<ActionResult<PaymentDto>> CreatePayment(CreatePaymentDto createDto)
        {
            // Issue #37: OrderId is a real int FK now, so a nonexistent order
            // must be rejected here (a clean 404 beats the FK DbUpdateException
            // at SaveChanges) — and the Order row is already in hand to put
            // CustomerId on the event for the push consumer.
            var order = await _context.Orders.FindAsync(createDto.OrderId);
            if (order == null)
            {
                _logger.LogWarning("CreatePayment rejected: Order {OrderId} not found.", createDto.OrderId);
                return NotFound(new { message = $"Order with ID {createDto.OrderId} not found." });
            }

            var payment = new Payment
            {
                PaymentId = Guid.NewGuid(),
                OrderId = createDto.OrderId,
                Amount = createDto.Amount,
                Method = createDto.PaymentMethod,
                Status = createDto.Status,
                TransactionCode = createDto.TransactionId,
                PaidDate = createDto.PaymentDate,
                Notes = createDto.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            // Publish PaymentReceived event
            try
            {
                var paymentReceivedEvent = new PaymentReceivedEvent
                {
                    PaymentId = payment.PaymentId.ToString(),
                    OrderId = payment.OrderId.ToString(),
                    Amount = payment.Amount,
                    PaymentMethod = payment.Method,
                    Status = payment.Status,
                    PaidDate = payment.PaidDate ?? payment.CreatedAt,
                    CreatedAt = payment.CreatedAt,
                    CustomerId = order.CustomerId // Issue #37: push via registry customer:<CustomerId>
                };

                var paymentReceivedQueue = _configuration["RabbitMQ:Queues:PaymentReceived"] ?? "payment.received";
                await _messagePublisher.PublishMessageAsync(paymentReceivedQueue, paymentReceivedEvent);
                _logger.LogInformation("Published PaymentReceived event for Payment {PaymentId}", payment.PaymentId);
            }
            catch (Exception ex)
            {
                // Log error but don't fail the request
                _logger.LogError(ex, "Error publishing PaymentReceived event for Payment {PaymentId}", payment.PaymentId);
            }

            var paymentDto = new PaymentDto
            {
                Id = payment.PaymentId,
                OrderId = payment.OrderId,
                Amount = payment.Amount,
                PaymentDate = payment.PaidDate ?? DateTime.MinValue,
                PaymentMethod = payment.Method,
                Status = payment.Status,
                TransactionId = payment.TransactionCode,
                Notes = payment.Notes,
                CreatedAt = payment.CreatedAt,
                UpdatedAt = payment.UpdatedAt
            };
            return CreatedAtAction(nameof(GetPayments), new { id = payment.PaymentId }, paymentDto);
        }
    }
}
