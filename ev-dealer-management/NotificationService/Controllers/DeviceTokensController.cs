using Microsoft.AspNetCore.Mvc;
using NotificationService.Services;

namespace NotificationService.Controllers;

/// <summary>
/// DeviceToken registry API (Issue #33). Keys are subjects, e.g.
/// "customer:42" — build them server-side only via NotificationSubjects;
/// nothing here parses or invents a key format.
/// Auth threat model (review-hardened): registration is anonymous like the
/// existing subscribe-topic endpoints, bounded by a per-subject cap
/// (DeviceTokenRegistry.MaxTokensPerSubject), and GET never returns raw
/// tokens — masked previews only, so keys can be probed but tokens cannot be
/// harvested (a stolen token is the ability to revoke that device). Residual
/// accepted risk until authenticated registration ships (follow-up issue):
/// anyone who guesses a subject can plant THEIR OWN token there and receive
/// that subject's pushes — mitigate by setting DeviceTokens:RegistrationKey.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DeviceTokensController : ControllerBase
{
    private readonly IDeviceTokenRegistry _registry;
    private readonly IConfiguration _config;

    public DeviceTokensController(IDeviceTokenRegistry registry, IConfiguration config)
    {
        _registry = registry;
        _config = config;
    }

    /// <summary>
    /// Interim gate for anonymous registration: when DeviceTokens:RegistrationKey
    /// is non-empty, PUT/DELETE require it in the X-Device-Registry-Key header.
    /// Empty (the dev default) keeps the Issue #33 acceptance flow working and
    /// logs a warning once per boot — auth is a product decision, not this
    /// controller's job.
    /// </summary>
    private IActionResult? RequireRegistrationKey()
    {
        var expected = _config["DeviceTokens:RegistrationKey"];
        if (string.IsNullOrWhiteSpace(expected)) return null;
        if (Request.Headers.TryGetValue("X-Device-Registry-Key", out var given) &&
            string.Equals(given.ToString(), expected, StringComparison.Ordinal))
            return null;
        return Unauthorized(new { message = "invalid or missing X-Device-Registry-Key" });
    }

    /// <summary>Register (upsert) a token for a subject. Idempotent, safe under
    /// concurrent registration/revoke races (no 500s).</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Register(string key, [FromBody] RegisterDeviceTokenRequest request, CancellationToken ct)
    {
        if (RequireRegistrationKey() is { } gate) return gate;
        if (string.IsNullOrWhiteSpace(request?.Token))
            return BadRequest(new { message = "token is required" });
        if (request.Token.Trim().Length > 8192)
            return BadRequest(new { message = "token too long" }); // FCM web tokens are ~few hundred bytes

        try
        {
            await _registry.RegisterAsync(key, request.Token, ct);
        }
        catch (DeviceTokenLimitExceededException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        return NoContent();
    }

    /// <summary>Live-token count + masked previews for a subject (registration
    /// UI check, ops probe). Raw tokens stay server-side by design — see the
    /// class doc for why.</summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> List(string key, CancellationToken ct)
    {
        var tokens = await _registry.GetTokensAsync(key, ct);
        return Ok(new { key, count = tokens.Count, tokens = tokens.Select(Mask).ToList() });
    }

    /// <summary>"tok-abcdefghij…" → "tok-a…ij (13 chars)": enough to confirm a
    /// registration landed without exporting the credential.</summary>
    private static string Mask(string token) =>
        token.Length <= 8 ? "••••" : $"{token[..4]}…{token[^4..]} ({token.Length} chars)";

    /// <summary>Revoke one token (logout / browser unregistration). Requires
    /// the exact token — masked previews are not accepted.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Revoke(string key, [FromQuery] string token, CancellationToken ct)
    {
        if (RequireRegistrationKey() is { } gate) return gate;
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "token query parameter is required" });

        var removed = await _registry.RevokeAsync(key, token, ct);
        return removed ? NoContent() : NotFound(new { message = "token not registered for this key" });
    }
}

public record RegisterDeviceTokenRequest(string Token);
