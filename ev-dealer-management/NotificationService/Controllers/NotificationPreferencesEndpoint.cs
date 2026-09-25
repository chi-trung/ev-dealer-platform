using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Services;

namespace NotificationService.Controllers;

/// <summary>
/// Issue #51 — per-subject notification preferences, appended to the
/// existing NotificationController (route api/Notification) because the
/// frontend calls /notifications/preferences and the gateway rewrites
/// /api/notifications/{everything} → /api/Notification/{everything}. The other
/// endpoints on this controller stay anonymous; these two are [Authorize] at
/// method level.
///
/// Auth model is Issue #36's, deliberately tightened: the subject key is NOT
/// input at all. It is rebuilt server-side as "user:&lt;id claim&gt;" from
/// the Bearer token, so the write-scope property ("you may only touch your
/// own preferences") holds by construction — there is no key to authorize,
/// nothing to spoof, and no "dealer:&lt;n&gt;" preferences surface exists
/// (this is a personal settings screen). A caller without a usable positive
/// int "id" claim gets 403.
///
/// Validation is exhaustive by design (the Issue #50 lesson): the PUT wire
/// DTO is all-nullable and every flag must be present — System.Text.Json
/// binds MISSING members to their default even under non-nullable
/// annotations, and a default `false` here means MUTED. Silently muting a
/// user's payments alert because some client forgot a field is exactly the
/// bug class; it is a 400 instead.
///
/// Scope note: #51 deliberately shipped persistence only — the enforcement
/// half landed in Issue #56, where NotificationPreferencePolicy reads these
/// documents at the user:&lt;id&gt; fan-out points (the quote/contract
/// consumers' salesperson pushes). In-app is the only channel FCM delivery
/// exercises today, so email/sms flags persist but gate nothing yet; the
/// frontend hides those two toggles until a sender exists, keeping the page
/// and the enforcement honest with each other.
/// </summary>
public partial class NotificationController
{
    // _preferencesStore comes from the (IFcmService, INotificationPreferencesStore?)
    // ctor in NotificationController.cs.

    /// <summary>The caller's personal preferences subject, built the only way
    /// allowed to exist. Null = no usable "id" claim → 403.</summary>
    private string? OwnSubjectOrNull()
        => int.TryParse(User.FindFirstValue("id"), out var userId) && userId > 0
            ? NotificationSubjects.User(userId)
            : null;

    private IActionResult ForbiddenNoSubject()
        => StatusCode(StatusCodes.Status403Forbidden,
            new { message = "a valid user id claim is required to manage notification preferences" });

    /// <summary>The caller's saved preferences, or the shared defaults when
    /// nothing has been saved yet (the honest answer to "you haven't chosen").
    /// Shape mirrors the frontend contract (channels + nested
    /// notificationTypes) so the page consumes it unchanged.</summary>
    [Authorize]
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        if (OwnSubjectOrNull() is not { } subject) return ForbiddenNoSubject();
        if (_preferencesStore is not { } store)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "preferences storage is unavailable" });

        var p = await store.GetAsync(subject, ct);
        return Ok(new { data = Shape(p), success = true });
    }

    /// <summary>Replace the caller's full preferences document. All eight
    /// flags are required (see class doc); last-writer-wins under
    /// concurrency — no partial PATCH semantics on a settings document.</summary>
    [Authorize]
    [HttpPut("preferences")]
    public async Task<IActionResult> PutPreferences([FromBody] PreferencesWireRequest? request, CancellationToken ct)
    {
        if (OwnSubjectOrNull() is not { } subject) return ForbiddenNoSubject();
        if (_preferencesStore is not { } store)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "preferences storage is unavailable" });
        if (request is null)
            return BadRequest(new { message = "a preferences document is required" });

        // Every flag must arrive explicitly. Nested nulls (notificationTypes:
        // null) bind even under [ApiController] validation — the recursive
        // pattern below rejects them the same way as missing scalars.
        if (request is not {
                EmailNotifications: { } email,
                SmsNotifications: { } sms,
                InAppNotifications: { } inApp,
                Types: { } types,
            }
            || types.Orders is not { } orders
            || types.Deliveries is not { } deliveries
            || types.Payments is not { } payments
            || types.System is not { } system
            || types.Promotions is not { } promotions)
        {
            return BadRequest(new
            {
                message = "all eight preference flags are required (emailNotifications, smsNotifications, inAppNotifications, notificationTypes.{orders,deliveries,payments,system,promotions}); " +
                          "a missing flag would be stored as muted, which the API refuses to do silently",
            });
        }

        var saved = await store.PutAsync(subject, new NotificationPreferencesDto
        {
            EmailNotifications = email,
            SmsNotifications = sms,
            InAppNotifications = inApp,
            Orders = orders,
            Deliveries = deliveries,
            Payments = payments,
            SystemAlerts = system,
            Promotions = promotions,
        }, ct);

        return Ok(new
        {
            data = Shape(saved),
            success = true,
            message = "Notification preferences updated successfully",
        });
    }

    private static object Shape(NotificationPreferencesDto p) => new
    {
        emailNotifications = p.EmailNotifications,
        smsNotifications = p.SmsNotifications,
        inAppNotifications = p.InAppNotifications,
        notificationTypes = new
        {
            orders = p.Orders,
            deliveries = p.Deliveries,
            payments = p.Payments,
            system = p.SystemAlerts,
            promotions = p.Promotions,
        },
    };

    public record TypesWireRequest(
        bool? Orders,
        bool? Deliveries,
        bool? Payments,
        bool? System,
        bool? Promotions);

    public record PreferencesWireRequest(
        bool? EmailNotifications,
        bool? SmsNotifications,
        bool? InAppNotifications,
        [property: System.Text.Json.Serialization.JsonPropertyName("notificationTypes")]
        TypesWireRequest? Types);
}
