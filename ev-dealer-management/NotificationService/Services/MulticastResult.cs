namespace NotificationService.Services;

/// <summary>
/// Outcome of one multicast attempt (Issue #44). The old <c>bool</c> return
/// could only say "something failed", which forced consumers to treat a dead
/// token exactly like a transient HTTP 503 — requeueing forever while the
/// registry filled up with tokens FCM had permanently rejected.
/// </summary>
/// <param name="Success">True when at least one token accepted the message —
/// same meaning the old bool carried, so the retry/DLQ contract is unchanged.</param>
/// <param name="DeadTokens">The subset of the sent tokens that FCM rejected
/// PERMANENTLY (<c>UNREGISTERED</c> / <c>INVALID_ARGUMENT</c> — see
/// <see cref="FirebaseFcmService.IsPermanentlyDead"/>). Callers that obtained
/// these tokens from the device-token registry should revoke them; a send that
/// never reached FCM (transport error, bad credentials, empty list) returns an
/// EMPTY list — "we don't know" must never be read as "the token is dead", or
/// one Firebase outage mass-unregisters every device in the fleet.</param>
public sealed record MulticastResult(bool Success, IReadOnlyList<string> DeadTokens)
{
    /// <summary>Total failure with no token blamed (empty list, transport
    /// error, exception inside FirebaseAdmin).</summary>
    public static MulticastResult Failed { get; } = new(false, Array.Empty<string>());
}
