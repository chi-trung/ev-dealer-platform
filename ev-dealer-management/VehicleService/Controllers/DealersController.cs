using Microsoft.AspNetCore.Mvc;
using VehicleService.DTOs;
using VehicleService.Services;

using Microsoft.AspNetCore.Authorization;
namespace VehicleService.Controllers;

[ApiController]
[Route("api/[controller]")]
// Issue #137: dealer reads stay anonymous -- UserService's DealerIdValidator
// fetches /api/dealers server-to-server during anonymous registration
// (UserService/Program.cs DealerIdValidator.GetDealersAsync), so gating the
// reads would break signup. Writes are admin-only below.
public class DealersController : ControllerBase
{
    private readonly IVehicleService _vehicleService;

    public DealersController(IVehicleService vehicleService)
    {
        _vehicleService = vehicleService;
    }

    [HttpGet]
    public async Task<ActionResult<List<DealerDto>>> GetDealers()
    {
        var dealers = await _vehicleService.GetDealersAsync();
        return Ok(dealers);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DealerDto>> GetDealer(int id)
    {
        var dealer = await _vehicleService.GetDealerByIdAsync(id);
        if (dealer == null)
        {
            return NotFound(new { message = "Dealer not found" });
        }
        return Ok(dealer);
    }

    // Issue #137: creating a dealer is admin-only -- dealer accounts are
    // scoped to it, so an anonymous dealer creation silently grants a
    // privileged context to whoever signs up against it.
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<DealerDto>> CreateDealer(CreateDealerDto createDto)
    {
        try
        {
            var dealer = await _vehicleService.CreateDealerAsync(createDto);
            return CreatedAtAction(nameof(GetDealer), new { id = dealer.Id }, dealer);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Issue #137: admin-only, same reason as creation -- a dealer rename or
    // contact change is what downstream user accounts bind to.
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<DealerDto>> UpdateDealer(int id, UpdateDealerDto updateDto)
    {
        try
        {
            var dealer = await _vehicleService.UpdateDealerAsync(id, updateDto);
            if (dealer == null)
            {
                return NotFound(new { message = "Dealer not found" });
            }
            return Ok(dealer);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Issue #137: admin-only -- deleting a dealer orphans every user account
    // whose DealerId points at it.
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteDealer(int id)
    {
        var deleted = await _vehicleService.DeleteDealerAsync(id);
        if (!deleted)
        {
            return NotFound(new { message = "Dealer not found" });
        }
        return NoContent();
    }
}
