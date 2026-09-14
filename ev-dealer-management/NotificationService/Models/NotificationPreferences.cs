namespace NotificationService.Models;

/// <summary>
/// One per-subject notification-preferences row (Issue #51). Mirrors the
/// Issue #33 DeviceToken registry: string subject keys, owned entirely by
/// this service, one row per subject (UNIQUE key — unlike tokens, a subject
/// has exactly ONE preference document).
///
/// The eight booleans mirror the frontend contract on
/// src/pages/Notifications/NotificationPreferences.jsx one-for-one: three
/// channels plus five event types. They are deliberately NOT a JSON blob —
/// explicit columns make the schema greppable and the PUT validation
/// exhaustive (every field is required; a missing or null field is a 400,
/// never a silent default-to-false, because these flags mean "mute" — the
/// Issue #50 lesson about missing members binding to defaults).
///
/// Scope note: this issue persists and serves the choices so the "saved"
/// message stops being a lie (reload no longer evaporates them). Consumers
/// honoring the muted channels/types is NOT wired here: today no consumer
/// fans out to "user:&lt;id&gt;" subjects at all (see the doc note on
/// NotificationSubjects.User), so any filtering code now would be dead.
/// That half is a separate issue.
/// </summary>
public class NotificationPreferences
{
    public int Id { get; set; }

    /// <summary>Subject key, e.g. "user:7". Built server-side from the JWT
    /// id claim only — never parsed from client input (Issue #36 pattern).</summary>
    public string Key { get; set; } = string.Empty;

    // ---- channels ----
    public bool EmailNotifications { get; set; }
    public bool SmsNotifications { get; set; }
    public bool InAppNotifications { get; set; }

    // ---- event types ----
    public bool Orders { get; set; }
    public bool Deliveries { get; set; }
    public bool Payments { get; set; }
    public bool SystemAlerts { get; set; }
    public bool Promotions { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
