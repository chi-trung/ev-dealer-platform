using Microsoft.AspNetCore.Mvc;
using SalesService.Data;
using SalesService.Models;
using SalesService.DTOs;
using SalesService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for IEnumerable

namespace SalesService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContractsController : ControllerBase
    {
        private readonly SalesDbContext _context;
        private readonly ILogger<ContractsController> _logger;
        private readonly IMessagePublisher _messagePublisher;
        private readonly IConfiguration _configuration;

        public ContractsController(SalesDbContext context, ILogger<ContractsController> logger, IMessagePublisher messagePublisher, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _messagePublisher = messagePublisher;
            _configuration = configuration;
        }

        /// <summary>
        /// Gets all contracts.
        /// </summary>
        [HttpGet] // Added this HttpGet endpoint
        public async Task<ActionResult<IEnumerable<Contract>>> GetAllContracts()
        {
            try
            {
                var contracts = await _context.Contracts.ToListAsync();
                _logger.LogInformation("Retrieved {Count} contracts.", contracts.Count);
                return Ok(contracts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all contracts from database.");
                return StatusCode(500, new { message = "Failed to retrieve contracts from database", error = ex.Message });
            }
        }

        /// <summary>
        /// Creates a new contract from an order.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateContract([FromBody] CreateContractRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Attempting to create contract for Order ID: {OrderId}", request.OrderId);

            var order = await _context.Orders.FindAsync(request.OrderId);
            if (order == null)
            {
                _logger.LogWarning("Order with ID {OrderId} not found.", request.OrderId);
                return NotFound(new { message = $"Order with ID {request.OrderId} not found." });
            }

            var existingContract = await _context.Contracts.FirstOrDefaultAsync(c => c.OrderId == request.OrderId);
            if (existingContract != null)
            {
                _logger.LogWarning("Contract for Order ID {OrderId} already exists.", request.OrderId);
                return BadRequest(new { message = $"A contract for order {request.OrderId} already exists." });
            }

            if (!int.TryParse(request.SalespersonId, out int salespersonId))
            {
                _logger.LogWarning("Invalid SalespersonId format: {SalespersonId}", request.SalespersonId);
                return BadRequest(new { message = "Invalid SalespersonId format." });
            }

            var contract = new Contract
            {
                OrderId = request.OrderId,
                CustomerId = request.CustomerId,
                DealerId = order.DealerId, // Get DealerId from the order
                SalespersonId = salespersonId, // Use the parsed int
                ContractNumber = $"CNTR-{DateTime.UtcNow:yyyyMMdd}-{order.OrderId}", // Generate a contract number
                SignedDate = DateOnly.FromDateTime(request.ContractDate),
                TotalAmount = order.TotalPrice,
                PaymentStatus = request.DepositAmountReceived ? "Partial" : "Unpaid",
                Status = "PendingApproval",
                Notes = request.TermsAndConditions, // Use Notes field to store terms
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            order.Status = "PendingApproval";
            _context.Orders.Update(order);
            _context.Contracts.Add(contract);

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully created contract {ContractId} for Order ID {OrderId}.", contract.ContractId, request.OrderId);

                // Publish ContractCreated event (NotificationService pushes it to the
                // customer's device). Mirrors OrdersController's publish pattern:
                // log-and-continue, never fail the request on a broker error.
                try
                {
                    var contractCreatedEvent = new ContractCreatedEvent
                    {
                        ContractId = contract.ContractId.ToString(),
                        ContractNumber = contract.ContractNumber,
                        OrderId = contract.OrderId,
                        CustomerId = contract.CustomerId,
                        DealerId = contract.DealerId,
                        SalespersonId = contract.SalespersonId,
                        TotalAmount = contract.TotalAmount,
                        PaymentStatus = contract.PaymentStatus,
                        Status = contract.Status,
                        CreatedAt = contract.CreatedAt
                    };

                    var contractCreatedQueue = _configuration["RabbitMQ:Queues:ContractCreated"] ?? "contract.created";
                    await _messagePublisher.PublishMessageAsync(contractCreatedQueue, contractCreatedEvent);
                    _logger.LogInformation("Published ContractCreated event for Contract {ContractId}.", contract.ContractId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error publishing ContractCreated event for Contract {ContractId}.", contract.ContractId);
                }
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database update failed when creating contract for Order ID {OrderId}.", request.OrderId);
                return StatusCode(500, new { message = "An error occurred while saving the contract.", error = ex.InnerException?.Message ?? ex.Message });
            }

            return CreatedAtAction(nameof(GetContractById), new { id = contract.ContractId }, contract);
        }

        /// <summary>
        /// Gets a contract by its ID.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<Contract>> GetContractById(int id)
        {
            var contract = await _context.Contracts.FindAsync(id);

            if (contract == null)
            {
                return NotFound();
            }

            return Ok(contract);
        }

        /// <summary>
        /// Updates the status of a contract.
        /// </summary>
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateContractStatus(int id, [FromBody] UpdateStatusRequest request)
        {
            _logger.LogInformation("Attempting to update status for Contract ID: {ContractId} to {Status}", id, request.Status);

            var contract = await _context.Contracts.Include(c => c.Order).FirstOrDefaultAsync(c => c.ContractId == id);
            if (contract == null)
            {
                _logger.LogWarning("Contract with ID {ContractId} not found.", id);
                return NotFound(new { message = $"Contract with ID {id} not found." });
            }

            var order = contract.Order;
            if (order == null)
            {
                _logger.LogWarning("Order associated with Contract ID {ContractId} not found.", id);
                return NotFound(new { message = "Associated order not found." });
            }

            contract.UpdatedAt = DateTime.UtcNow;

            // If rejected, remove both the contract and its order so the order no longer appears in the sales list
            if (request.Status == "Rejected")
            {
                _logger.LogInformation("Contract {ContractId} rejected; removing related order {OrderId}.", id, order.OrderId);
                // Issue #37 review: Payments.OrderId is now a real NOT NULL FK with
                // DeleteBehavior.Restrict (before it, the Guid/shadow-OrderId1 link
                // constrained nothing, so deleting an Order "succeeded" with
                // payments silently orphaned). The rejection flow must therefore
                // delete the order's payments explicitly — dependents before
                // principal, in the same SaveChanges — or SQLite's FK enforcement
                // fails the DELETE with "FOREIGN KEY constraint failed" and every
                // paid order becomes un-rejectable (500).
                var payments = await _context.Payments.Where(p => p.OrderId == order.OrderId).ToListAsync();
                _context.Contracts.Remove(contract);
                _context.Payments.RemoveRange(payments);
                _context.Orders.Remove(order);

                try
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Contract {ContractId} and Order {OrderId} removed after rejection.", id, order.OrderId);
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Failed to delete order {OrderId} when rejecting contract {ContractId}.", order.OrderId, id);
                    return StatusCode(500, new { message = "An error occurred while removing the rejected order.", error = ex.InnerException?.Message ?? ex.Message });
                }

                return Ok(new { message = "Hợp đồng đã bị từ chối và đơn hàng liên quan đã được xóa." });
            }

            // Update contract/order status for non-rejection scenarios
            contract.Status = request.Status;

            if (request.Status == "Approved")
            {
                order.Status = "ReadyForDelivery";
            }
            else
            {
                order.Status = request.Status;
            }
            order.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully updated status for Contract ID {ContractId} and its Order.", id);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database update failed when updating status for Contract ID {ContractId}.", id);
                return StatusCode(500, new { message = "An error occurred while updating the status.", error = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { message = "Contract status updated successfully." });
        }
    }
}
