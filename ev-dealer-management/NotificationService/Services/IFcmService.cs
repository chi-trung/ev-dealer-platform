namespace NotificationService.Services;

public interface IFcmService
{
    /// <summary>
    /// Send notification to a specific device token
    /// </summary>
    Task<bool> SendNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null);

    /// <summary>
    /// Send notification to a topic (broadcast to all subscribers)
    /// </summary>
    Task<bool> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null);

    /// <summary>
    /// Send notification to multiple device tokens at once. The result carries
    /// the permanently-dead subset of <paramref name="deviceTokens"/> so
    /// registry-backed callers can evict stale rows instead of letting a
    /// shared subject (dealer:&lt;id&gt;, Issue #44) fill to its cap with
    /// tokens FCM already rejected. Tokens are echoed back unmasked — the
    /// caller supplied them in the first place; do not log them raw.
    /// </summary>
    Task<MulticastResult> SendMulticastAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null);

    /// <summary>
    /// Subscribe a device token to a topic
    /// </summary>
    Task<bool> SubscribeToTopicAsync(string deviceToken, string topic);

    /// <summary>
    /// Unsubscribe a device token from a topic
    /// </summary>
    Task<bool> UnsubscribeFromTopicAsync(string deviceToken, string topic);
}
