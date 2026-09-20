using Microsoft.AspNetCore.Mvc;
using VehicleService.Services;

namespace VehicleService.Controllers;

[ApiController]
[Route("api/[controller]")]
// Issue #137: deliberately anonymous -- the vehicle-type list populates the
// public catalogue filter dropdowns, and VehicleService.Services depends on
// it being reachable.
public class VehicleTypesController : ControllerBase
{
    private readonly IVehicleService _vehicleService;

    public VehicleTypesController(IVehicleService vehicleService)
    {
        _vehicleService = vehicleService;
    }

    [HttpGet]
    public async Task<IActionResult> GetVehicleTypes()
    {
        var types = await _vehicleService.GetVehicleTypesAsync();
        return Ok(types);
    }
}
