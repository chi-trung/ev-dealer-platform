using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Linq;
using VehicleService.DTOs;
using VehicleService.Services;

using Microsoft.AspNetCore.Authorization;
namespace VehicleService.Controllers;

[ApiController]
[Route("api/[controller]")]
// Issue #137: the catalogue reads below are the public shop window the
// frontend's vehicle list renders before login, so they stay anonymous.
// Controller-level [Authorize] would close them too; each mutation carries
// its own attribute instead.
public class VehiclesController : ControllerBase
{
    private readonly IVehicleService _vehicleService;

    public VehiclesController(IVehicleService vehicleService)
    {
        _vehicleService = vehicleService;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<VehicleDto>>> GetVehicles([FromQuery] VehicleQueryDto query)
    {
        var result = await _vehicleService.GetVehiclesAsync(query);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VehicleDto>> GetVehicle(int id)
    {
        var vehicle = await _vehicleService.GetVehicleByIdAsync(id);
        if (vehicle == null)
        {
            return NotFound(new { message = "Vehicle not found" });
        }
        return Ok(vehicle);
    }

    // Issue #137: creating a listing changes inventory and uploads images;
    // an anonymous caller could fill the catalogue or store arbitrary files.
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<VehicleDto>> CreateVehicle()
    {
        try
        {
            CreateVehicleDto createDto = null!;
            List<IFormFile>? imageFiles = null;

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                imageFiles = form.Files?.ToList();

                createDto = new CreateVehicleDto
                {
                    Model = form["Model"].FirstOrDefault() ?? string.Empty,
                    Type = form["Type"].FirstOrDefault() ?? string.Empty,
                    Price = decimal.TryParse(form["Price"].FirstOrDefault(), out var p) ? p : 0,
                    BatteryCapacity = double.TryParse(form["BatteryCapacity"].FirstOrDefault(), out var b) ? b : 0,
                    Range = int.TryParse(form["Range"].FirstOrDefault(), out var r) ? r : 0,
                    StockQuantity = int.TryParse(form["StockQuantity"].FirstOrDefault(), out var s) ? s : 0,
                    Description = form["Description"].FirstOrDefault(),
                    DealerId = int.TryParse(form["DealerId"].FirstOrDefault(), out var d) ? d : 0
                };
            }
            else
            {
                createDto = await HttpContext.Request.ReadFromJsonAsync<CreateVehicleDto>();
                // no files when JSON
                imageFiles = null;
            }

            if (createDto == null)
            {
                return BadRequest(new { message = "Invalid payload" });
            }

            if (!TryValidateModel(createDto))
            {
                return ValidationProblem(ModelState);
            }

            var vehicle = await _vehicleService.CreateVehicleAsync(createDto, imageFiles);
            return CreatedAtAction(nameof(GetVehicle), new { id = vehicle.Id }, vehicle);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Issue #137: edit access to a listing (price, status, images).
    [HttpPut("{id}")]
    [Authorize]
    public async Task<ActionResult<VehicleDto>> UpdateVehicle(int id)
    {
        try
        {
            UpdateVehicleDto updateDto = null!;
            List<IFormFile>? imageFiles = null;

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                imageFiles = form.Files?.ToList();

                updateDto = new UpdateVehicleDto
                {
                    Model = form["Model"].FirstOrDefault() ?? string.Empty,
                    Type = form["Type"].FirstOrDefault() ?? string.Empty,
                    Price = decimal.TryParse(form["Price"].FirstOrDefault(), out var p) ? p : 0,
                    BatteryCapacity = double.TryParse(form["BatteryCapacity"].FirstOrDefault(), out var b) ? b : 0,
                    Range = int.TryParse(form["Range"].FirstOrDefault(), out var r) ? r : 0,
                    StockQuantity = int.TryParse(form["StockQuantity"].FirstOrDefault(), out var s) ? s : 0,
                    Description = form["Description"].FirstOrDefault(),
                    DealerId = int.TryParse(form["DealerId"].FirstOrDefault(), out var d) ? d : 0
                };
            }
            else
            {
                updateDto = await HttpContext.Request.ReadFromJsonAsync<UpdateVehicleDto>();
                imageFiles = null;
            }

            if (updateDto == null)
            {
                return BadRequest(new { message = "Invalid payload" });
            }

            if (!TryValidateModel(updateDto))
            {
                return ValidationProblem(ModelState);
            }

            var vehicle = await _vehicleService.UpdateVehicleAsync(id, updateDto, imageFiles);
            if (vehicle == null)
            {
                return NotFound(new { message = "Vehicle not found" });
            }
            return Ok(vehicle);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Issue #137: deleting a listing removes inventory.
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteVehicle(int id)
    {
        try
        {
            var deleted = await _vehicleService.DeleteVehicleAsync(id);
            if (!deleted)
            {
                return NotFound(new { message = "Vehicle not found" });
            }
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Failed to delete vehicle: " + ex.Message });
        }
    }

    // Issue #137: reserving a vehicle mutates its availability for other
    // buyers. The frontend only ever calls this from an authenticated page.
    [HttpPost("{id}/reserve")]
    [Authorize]
    public async Task<ActionResult> ReserveVehicle(int id, [FromBody] ReservationRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _vehicleService.ReserveVehicleAsync(id, request);
            if (result == null)
            {
                return NotFound(new { message = "Vehicle not found or insufficient stock" });
            }

            return Ok(new
            {
                message = "Vehicle reserved successfully",
                reservation = result
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
