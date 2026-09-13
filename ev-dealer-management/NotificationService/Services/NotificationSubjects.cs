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
}
