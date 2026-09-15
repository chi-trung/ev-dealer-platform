namespace NotificationService.Services;

/// <summary>
/// The ONE spelling of registry subject keys. Consumers (delivery side) and
/// the registration API must agree — both go through here so a format
/// change is a one-line diff, not a silent registry-wide miss (a key typo
/// fails as "no tokens" and the event degrades to log-only, exactly the
/// silent-drop class docs/EVENTS.md documents).
/// </summary>
public static class NotificationSubjects
{
    /// <summary>Customer-domain subject, e.g. "customer:42".</summary>
    public static string Customer(int customerId) => $"customer:{customerId}";

    /// <summary>Portal-account subject, e.g. "user:7" — the key a logged-in
    /// staff member may register (Issue #36: "id" claim ⇒ exactly this key).
    /// Since Issue #56 this is ALSO a fan-out key: the quote/contract
    /// consumers push to the assigned salesperson here, gated by
    /// NotificationPreferencePolicy (the only subject family with a
    /// preferences surface).</summary>
    public static string User(int userId) => $"user:{userId}";

    /// <summary>Dealer-domain subject, e.g. "dealer:3" — registrable only when
    /// the JWT carries a matching "dealer" claim (Issue #36); the vehicle
    /// lifecycle events fan their pushes out here (Issue #38).</summary>
    public static string Dealer(int dealerId) => $"dealer:{dealerId}";
}
