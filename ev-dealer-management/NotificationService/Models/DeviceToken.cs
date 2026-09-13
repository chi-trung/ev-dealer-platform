namespace NotificationService.Models;

/// <summary>
/// One web-push registration token for one notification subject (Issue #33).
/// Keys are strings, not FKs — the token registry deliberately owns no
/// customer identity (UserService has no CustomerId link); whoever resolves
/// an event to a subject (consumer code) and whoever registers a browser
/// (login/pairing flow) must agree on the key format, e.g. "customer:42".
/// A subject can have several live tokens (multi-device browsers), so the
/// key is indexed but NOT unique; sending fans out over all rows.
/// </summary>
public class DeviceToken
{
    public int Id { get; set; }

    /// <summary>Subject key, e.g. "customer:42". Trimmed at the service layer.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>FCM registration token as minted by firebase/messaging.js.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
