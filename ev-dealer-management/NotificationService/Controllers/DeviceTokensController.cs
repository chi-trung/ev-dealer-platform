using Microsoft.AspNetCore.Mvc;
using NotificationService.Services;

namespace NotificationService.Controllers;

/// <summary>
/// DeviceToken registry API (Issue #33). Keys are subjects, e.g.
/// "customer:42" — build them server-side only via NotificationSubjects;
/// nothing here parses or invents a key format.
/// Auth threat model: a leaked web-push token lets an attacker send
/// notifications TO that device only (same exposure as the existing
/// subscribe-topic endpoints, which are anonymous too). Tighten both
/// together if a token-scoping auth story is ever designed.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DeviceTokensController : ControllerBase
{
    private readonly IDeviceTokenRegistry _registry;

    public DeviceTokensController(IDeviceTokenRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Register (upsert) a token for a subject. Idempotent.</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Register(string key, [FromBody] RegisterDeviceTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Token))
            return BadRequest(new { message = "token is required" });
        if (request.Token.Trim().Length > 8192)
            return BadRequest(new { message = "token too long" }); // FCM web tokens are ~few hundred bytes

        await _registry.RegisterAsync(key, request.Token, ct);
        return NoContent();
    }

    /// <summary>Live tokens for a subject (registration UI check, ops probe).</summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> List(string key, CancellationToken ct)
    {
        var tokens = await _registry.GetTokensAsync(key, ct);
        return Ok(new { key, tokens });
    }

    /// <summary>Revoke one token (logout / browser unregistration).</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Revoke(string key, [FromQuery] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "token query parameter is required" });

        var removed = await _registry.RevokeAsync(key, token, ct);
        return removed ? NoContent() : NotFound(new { message = "token not registered for this key" });
    }
}

public record RegisterDeviceTokenRequest(string Token);
