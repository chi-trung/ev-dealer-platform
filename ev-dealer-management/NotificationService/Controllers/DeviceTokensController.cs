using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Services;

namespace NotificationService.Controllers;

/// <summary>
/// DeviceToken registry API (Issue #33, authenticated since Issue #36). Keys
/// are subjects — build them server-side only via NotificationSubjects;
/// nothing here parses or invents a key format.
///
/// Auth model: endpoints require a UserService JWT (Bearer). A caller may
/// touch only their OWN subjects — "user:&lt;id&gt;" where &lt;id&gt; is the
/// token's "id" claim, and "dealer:&lt;n&gt;" only when the token also carries
/// a "dealer" claim equal to &lt;n&gt; (minted at login when the account has a
/// DealerId). Anything else — another user's mailbox, an arbitrary
/// "customer:…" guess — is 403. That replaces the Issue #33 accepted risk
/// ("anyone who guesses a subject can plant their own token there") and the
/// interim X-Device-Registry-Key gate, which is deleted.
///
/// Hard bounds: the per-subject cap (MaxTokensPerSubject) is enforced by
/// EVICTION, not rejection — a registration at the cap evicts the
/// least-recently-refreshed token and still returns 204 (Issue #44: shared
/// dealer subjects used to wedge at the cap and silently drop push for real
/// devices, and the frontend's PUT treats any non-2xx as fatal-to-that
/// device). Masked GET previews stay unchanged — even your own subject never
/// exports raw tokens, because responses pass through logs and browser
/// history. DELETE still demands the exact raw token for the same reason.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DeviceTokensController : ControllerBase
{
    private readonly IDeviceTokenRegistry _registry;

    public DeviceTokensController(IDeviceTokenRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// The caller's write scope. Returns null when <paramref name="key"/> is
    /// the caller's own subject, otherwise a 403 result. Compared against
    /// NotificationSubjects output (exact strings) — not a prefix parse — so
    /// "user:12" can't be spoofed by e.g. "user:12 " whitespace tricks at the
    /// registry layer; the registry itself trims keys, so we trim before
    /// comparing and the stored spelling is the trimmed one either way.
    /// </summary>
    private IActionResult? AuthorizeSubject(string key)
    {
        var subject = (key ?? "").Trim();

        // Own account mailbox: user:<id claim>.
        var userIdClaim = User.FindFirstValue("id");
        if (int.TryParse(userIdClaim, out var userId) &&
            subject == NotificationSubjects.User(userId))
            return null;

        // Dealer mailbox: dealer:<n> iff the token carries dealer:<n>.
        // int-parsing both sides (not string compare) keeps the spelling of
        // NotificationSubjects.Dealer authoritative; a malformed claim just
        // doesn't match.
        var dealerClaim = User.FindFirstValue("dealer");
        if (int.TryParse(dealerClaim, out var dealerId) &&
            subject == NotificationSubjects.Dealer(dealerId))
            return null;

        return StatusCode(StatusCodes.Status403Forbidden,
            new { message = "you may only manage device tokens for your own user and dealer subjects" });
    }

    /// <summary>Register (upsert) a token for one of the caller's subjects.
    /// Idempotent, safe under concurrent registration/revoke races (no 500s).</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Register(string key, [FromBody] RegisterDeviceTokenRequest request, CancellationToken ct)
    {
        if (AuthorizeSubject(key) is { } forbidden) return forbidden;
        if (string.IsNullOrWhiteSpace(request?.Token))
            return BadRequest(new { message = "token is required" });
        if (request.Token.Trim().Length > 8192)
            return BadRequest(new { message = "token too long" }); // FCM web tokens are ~few hundred bytes

        try
        {
            await _registry.RegisterAsync(key, request.Token, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        return NoContent();
    }

    /// <summary>Live-token count + masked previews for one of the caller's
    /// subjects (registration UI check, ops probe). Raw tokens stay
    /// server-side by design — see the class doc for why.</summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> List(string key, CancellationToken ct)
    {
        if (AuthorizeSubject(key) is { } forbidden) return forbidden;
        var tokens = await _registry.GetTokensAsync(key, ct);
        return Ok(new { key, count = tokens.Count, tokens = tokens.Select(Mask).ToList() });
    }

    /// <summary>"tok-abcdefghij…" → "tok-a…ij (13 chars)": enough to confirm a
    /// registration landed without exporting the credential.</summary>
    private static string Mask(string token) =>
        token.Length <= 8 ? "••••" : $"{token[..4]}…{token[^4..]} ({token.Length} chars)";

    /// <summary>Revoke one of your own tokens (logout / browser
    /// unregistration). Requires the exact token — masked previews are not
    /// accepted.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Revoke(string key, [FromQuery] string token, CancellationToken ct)
    {
        if (AuthorizeSubject(key) is { } forbidden) return forbidden;
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "token query parameter is required" });

        var removed = await _registry.RevokeAsync(key, token, ct);
        return removed ? NoContent() : NotFound(new { message = "token not registered for this key" });
    }
}

public record RegisterDeviceTokenRequest(string Token);
