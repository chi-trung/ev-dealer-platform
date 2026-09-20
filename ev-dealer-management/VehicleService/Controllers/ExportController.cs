using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using VehicleService.Data;
using VehicleService.Models;

using Microsoft.AspNetCore.Authorization;
namespace VehicleService.Controllers;

[ApiController]
[Route("api/[controller]")]
// Issue #137: the export dumps the full inventory with dealer and image
// metadata into a downloadable file -- staff data, not a public feed.
[Authorize]
public class ExportController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ExportController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("vehicles/csv")]
    public async Task<IActionResult> ExportVehiclesToCsv()
    {
        var vehicles = await _context.Vehicles
            .Include(v => v.Dealer)
            .Include(v => v.Images)
            .Include(v => v.ColorVariants)
            .Include(v => v.Specifications)
            .ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("Id,Model,Type,Price,BatteryCapacity,Range,StockQuantity,Description,DealerName,DealerRegion,CreatedAt,UpdatedAt");

        foreach (var vehicle in vehicles)
        {
            csv.AppendLine($"{vehicle.Id},{vehicle.Model},{vehicle.Type},{vehicle.Price},{vehicle.BatteryCapacity},{vehicle.Range},{vehicle.StockQuantity},\"{vehicle.Description}\",\"{vehicle.Dealer?.Name}\",\"{vehicle.Dealer?.Region}\",{vehicle.CreatedAt},{vehicle.UpdatedAt}");
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "vehicles_export.csv");
    }

    [HttpGet("vehicles/json")]
    public async Task<IActionResult> ExportVehiclesToJson()
    {
        var vehicles = await _context.Vehicles
            .Include(v => v.Dealer)
            .Include(v => v.Images)
            .Include(v => v.ColorVariants)
            .Include(v => v.Specifications)
            .Select(v => new
            {
                v.Id,
                v.Model,
                v.Type,
                v.Price,
                v.BatteryCapacity,
                v.Range,
                v.StockQuantity,
                v.Description,
                Dealer = new
                {
                    v.Dealer!.Name,
                    v.Dealer.Region,
                    v.Dealer.Contact,
                    v.Dealer.Email
                },
                Images = v.Images.Select(i => new { i.Url, i.AltText }),
                ColorVariants = v.ColorVariants.Select(c => new { c.Name, c.Hex, c.Stock }),
                Specifications = v.Specifications != null ? new
                {
                    v.Specifications.Acceleration,
                    v.Specifications.TopSpeed,
                    v.Specifications.Charging,
                    v.Specifications.Warranty,
                    v.Specifications.Seats,
                    v.Specifications.Cargo
                } : null,
                v.CreatedAt,
                v.UpdatedAt
            })
            .ToListAsync();

        return Ok(vehicles);
    }
}
