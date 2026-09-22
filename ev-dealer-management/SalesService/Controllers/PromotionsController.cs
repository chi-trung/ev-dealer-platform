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
    // Issue #137: promotions change pricing shown to customers, so only staff
    // may create or list them.
    [Authorize]
    public class PromotionsController : ControllerBase
    {
        private readonly SalesDbContext _context;

        public PromotionsController(SalesDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PromotionDto>>> GetPromotions()
        {
            var promotions = await _context.Promotions
                .Select(p => new PromotionDto
                {
                    Id = p.PromotionId,
                    Name = p.Code,
                    Description = p.Description,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    DiscountValue = p.Value,
                    DiscountType = p.Type,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                })
                .ToListAsync();
            return Ok(promotions);
        }

        [HttpPost]
        public async Task<ActionResult<PromotionDto>> CreatePromotion(CreatePromotionDto createDto)
        {
            var promotion = new Promotion
            {
                PromotionId = Guid.NewGuid(),
                Code = createDto.Name,
                Description = createDto.Description,
                Type = createDto.DiscountType,
                Value = createDto.DiscountValue,
                StartDate = createDto.StartDate,
                EndDate = createDto.EndDate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Promotions.Add(promotion);
            await _context.SaveChangesAsync();

            var promotionDto = new PromotionDto { /* map properties */ };
            return CreatedAtAction(nameof(GetPromotions), new { id = promotion.PromotionId }, promotionDto);
        }
    }
}
