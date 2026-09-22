using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;
namespace VehicleService.Controllers;

[ApiController]
[Route("api/[controller]")]
// Issue #137: the liveness/ready probes are polled by the gateway and
// Render's own health check, neither of which carries a token. The dedicated
// /health endpoint (MapHealthChecks, unauthenticated) is the real readiness
// signal; these are its legacy siblings.
[AllowAnonymous]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok("Healthy");
    }

    [HttpGet("ready")]
    public IActionResult GetReady()
    {
        return Ok("Ready");
    }

    [HttpGet("live")]
    public IActionResult GetLive()
    {
        return Ok("Live");
    }
}
