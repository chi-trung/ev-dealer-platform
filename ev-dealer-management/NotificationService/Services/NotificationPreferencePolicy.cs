using NotificationService.Services;
using Serilog;

namespace NotificationService.Services;

/// <summary>
/// Issue #56: the one place that turns a saved preference document into a
/// yes/no for "may this push be delivered to this subject". Created by
/// #51 as a settings surface with no enforcement; this is the half that
/// makes the saved flags actually mute pushes.
///
/// Scope — deliberately narrow: enforcement applies ONLY to "user:&lt;n&gt;"
/// subjects. The five type flags are per-user preference documents
/// (the settings screen is a personal one; see #51's auth model), and the
/// store is keyed exactly the way NotificationSubjects.User builds keys.
/// customer:/dealer: subjects have no preferences surface, so the policy
/// never consults the store for them and their delivery behavior is
/// bit-for-bit unchanged.
///
/// Channel flags (email/sms/in-app): today FCM push is the service's only
/// delivery channel (there is no email/SMS code path in NotificationService),
/// and an FCM push to a signed-in portal user is what the settings page
/// calls an "in-app" notification. So for user: subjects the push is gated
/// on InAppNotifications; email/sms stay stored-but-inert until a producer
/// for those channels exists — the frontend hides them meanwhile (#56
/// acceptance: UI and enforcement must match).
///
/// Type mapping (consumer data["type"] → flag), documented per #56
/// acceptance and in README.md:
///   quote, order, sale, contract -> Orders
///   orderStatus (delivery-state changes) -> Deliveries
///   payment -> Payments
///   customer, testdrive, vehicleCreated/Updated/Deleted -> SystemAlerts
///   (no producer exists yet that tags "promotion" — the Promotions flag
///    is stored and mapped in code so the day one ships it is a one-liner)
///   unknown tag -> DELIVER (fail open; see below)
/// Defaults: a user who never saved anything gets the shared defaults
/// (orders/deliveries/payments on, system/promotions off — the store's
/// GetAsync contract), so opted-out-of-nothing users are unchanged EXCEPT
/// that system-tagged pushes are new user:-targeted traffic and default
/// muted by that same table — which is the honest reading of "you haven't
/// chosen, the defaults apply".
///
/// Failure mode: fail OPEN. A preferences-store outage must not silently
/// mute every user-targeted push — mass suppression is indistinguishable
/// from the silent-drop class docs/EVENTS.md exists to prevent, whereas
/// delivering something the user muted is visible, recoverable, and the
/// same shape the old pre-#56 world had. The outage is logged loudly.
///
/// This class never throws on the store read. Callers may treat every
/// "false" it returns as a decision the user actually made.
/// </summary>
public interface INotificationPreferencePolicy
{
    /// <summary>May an FCM push tagged <paramref name="typeTag"/> (the
    /// consumer's data["type"]) be delivered to <paramref name="subject"/>?
    /// True for every non-user subject; for user:&lt;n&gt; it is the saved
    /// (or default) channel+type decision. Throws nothing — store outages
    /// are absorbed as fail-open.</summary>
    Task<bool> ShouldDeliverAsync(string subject, string typeTag, CancellationToken ct = default);
}

public sealed class NotificationPreferencePolicy : INotificationPreferencePolicy
{
    /// <summary>The one canonical spelling of consumer type tags (mirrors the
    /// data["type"] values grepped from Consumers/*.cs). Unknown tags fail
    /// open on purpose — a new consumer shipping a new tag must not be
    /// muted-by-absence before this table learns about it.</summary>
    private static readonly Dictionary<string, Func<NotificationPreferencesDto, bool>> TypeFlags =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["quote"] = p => p.Orders,
            ["order"] = p => p.Orders,
            ["sale"] = p => p.Orders,
            ["contract"] = p => p.Orders,
            ["orderStatus"] = p => p.Deliveries,
            ["payment"] = p => p.Payments,
            ["customer"] = p => p.SystemAlerts,
            ["testdrive"] = p => p.SystemAlerts,
            ["vehicleCreated"] = p => p.SystemAlerts,
            ["vehicleUpdated"] = p => p.SystemAlerts,
            ["vehicleDeleted"] = p => p.SystemAlerts,
            ["promotion"] = p => p.Promotions,
            ["promotions"] = p => p.Promotions,
        };

    private readonly INotificationPreferencesStore _store;

    public NotificationPreferencePolicy(INotificationPreferencesStore store)
    {
        _store = store;
    }

    public async Task<bool> ShouldDeliverAsync(string subject, string typeTag, CancellationToken ct = default)
    {
        // Only "user:<positive int>" has preferences. A non-int suffix can't
        // have been produced by NotificationSubjects.User, and a negative id
        // never exists in UserService — both are registry-pathological and
        // out of scope.
        if (!IsUserSubject(subject, out _)) return true;

        NotificationPreferencesDto prefs;
        try
        {
            prefs = await _store.GetAsync(subject, ct);
        }
        catch (Exception ex)
        {
            // Fail OPEN on purpose (class doc): delivering a muted-by-default
            // push during an outage beats mass-suppressing every user push
            // and looking exactly like the silent-drop failure mode.
            Log.Warning(ex,
                "🔔 Preferences store unavailable for {Subject}; delivering fail-open (notification NOT suppressed by an outage)",
                subject);
            return true;
        }

        if (!prefs.InAppNotifications)
        {
            Log.Information("🔕 Push suppressed for {Subject}: in-app channel muted (type: {Type})", subject, typeTag);
            return false;
        }

        // An unmapped tag means a consumer shipped a type this table doesn't
        // know yet — deliver (fail open), so enforcement can never mute
        // traffic by absence.
        if (!TypeFlags.TryGetValue(typeTag ?? "", out var flag))
        {
            Log.Debug("🔔 Unknown notification type tag '{Tag}' for {Subject}; delivering (unmapped ≠ muted)", typeTag, subject);
            return true;
        }

        if (flag(prefs)) return true;

        Log.Information("🔕 Push suppressed for {Subject}: type '{Type}' muted in preferences", subject, typeTag);
        return false;
    }

    /// <summary>True exactly when <paramref name="subject"/> is a user
    /// subject built by NotificationSubjects.User ("user:&lt;n&gt;",
    /// int-parsed both sides like the registration auth compare does).</summary>
    public static bool IsUserSubject(string? subject, out int userId)
    {
        userId = 0;
        return subject is not null
            && subject.StartsWith("user:", StringComparison.Ordinal)
            && int.TryParse(subject["user:".Length..], out userId)
            && userId > 0;
    }
}
