using Microsoft.AspNetCore.Mvc;
using SalesService.Data;
using SalesService.DTOs;
using SalesService.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
namespace SalesService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Issue #137: delivery records are fulfilment data behind an order; the
    // frontend reaches them only from the authenticated sales pages.
    [Authorize]
    public class DeliveriesController : ControllerBase
    {
        private readonly SalesDbContext _context;

        public DeliveriesController(SalesDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DeliveryDto>>> GetDeliveries()
        {
            var deliveries = await _context.Deliveries
                .Select(d => new DeliveryDto
                {
                    Id = d.DeliveryId,
                    OrderId = d.OrderId,
                    TrackingNumber = "", // Assuming TrackingNumber is not in the model
                    EstimatedDeliveryDate = d.DeliveryDate ?? DateTime.MinValue,
                    ActualDeliveryDate = d.DeliveryDate,
                    Status = d.Status,
                    Notes = d.Notes,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = d.UpdatedAt
                })
                .ToListAsync();
            return Ok(deliveries);
        }

        [HttpPost]
        public async Task<ActionResult<DeliveryDto>> CreateDelivery(CreateDeliveryDto createDto)
        {
            var delivery = new Delivery
            {
                DeliveryId = Guid.NewGuid(),
                OrderId = createDto.OrderId,
                DeliveryDate = createDto.EstimatedDeliveryDate,
                Status = createDto.Status,
                Notes = createDto.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Deliveries.Add(delivery);
            await _context.SaveChangesAsync();

            var deliveryDto = new DeliveryDto { /* map properties */ };
            return CreatedAtAction(nameof(GetDeliveries), new { id = delivery.DeliveryId }, deliveryDto);
        }
    }
}
