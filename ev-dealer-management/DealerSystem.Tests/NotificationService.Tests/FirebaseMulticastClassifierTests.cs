using FirebaseAdmin.Messaging;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #44: which per-token FCM error codes justify revoking a registry
/// row. SendMulticastAsync reads the code off FirebaseMessagingException via
/// the public send.Exception?.MessagingErrorCode property; the DECISION of
/// what counts as permanently dead is FirebaseFcmService.IsPermanentlyDead,
/// factored out precisely because neither SendResponse nor
/// FirebaseMessagingException is constructible from outside FirebaseAdmin
/// (its only 5-arg ctor is internal), so the classification is the testable
/// seam. Getting this wrong is asymmetric: a false positive mass-unregisters
/// live devices the first time a transient error code appears (or a
/// misconfigured sender id wipes every mailbox); a false negative only keeps
/// a dead token around, which LRU eviction eventually clears anyway.
/// </summary>
public class FirebaseMulticastClassifierTests
{
    [Theory]
    [InlineData(MessagingErrorCode.Unregistered, true)]      // unsubscribed/expired: dead
    [InlineData(MessagingErrorCode.InvalidArgument, true)]   // stale web-push keys: dead (issue scope)
    [InlineData(MessagingErrorCode.SenderIdMismatch, false)] // our config is wrong, not the device
    [InlineData(MessagingErrorCode.QuotaExceeded, false)]    // rate limit; retry will succeed
    [InlineData(MessagingErrorCode.Internal, false)]         // FCM hiccup
    [InlineData(MessagingErrorCode.Unavailable, false)]      // outage; the mass-revoke trap
    [InlineData(MessagingErrorCode.ThirdPartyAuthError, false)] // our credential, not the token
    public void IsPermanentlyDead_MatchesTheRevocationPolicy(MessagingErrorCode code, bool dead)
    {
        Assert.Equal(dead, FirebaseFcmService.IsPermanentlyDead(code));
    }

    [Fact]
    public void IsPermanentlyDead_NullCode_IsKept()
    {
        // Failures without a MessagingErrorCode (transport-level response,
        // send.Exception itself null) are "we don't know" — never dead.
        Assert.False(FirebaseFcmService.IsPermanentlyDead(null));
    }

    [Fact]
    public void MessagingErrorCodeEnum_IsFullyCoveredByThePolicy()
    {
        // The Theory above lists every member by hand; this pins that the
        // list stays exhaustive in BOTH directions: a FirebaseAdmin upgrade
        // that adds a code (8th member) or drops one fails HERE first,
        // forcing an explicit dead/keep decision instead of an unlisted
        // member silently defaulting to keep. A length check alone cannot
        // see growth (>= 7 stays true at 8 members) — so compare the actual
        // enum surface against the hand-listed set as a symmetric difference.
        var listed = (MessagingErrorCode[])Enum.GetValues(typeof(MessagingErrorCode));
        var pinned = new HashSet<MessagingErrorCode>
        {
            MessagingErrorCode.Unregistered, MessagingErrorCode.InvalidArgument,
            MessagingErrorCode.SenderIdMismatch, MessagingErrorCode.QuotaExceeded,
            MessagingErrorCode.Internal, MessagingErrorCode.Unavailable,
            MessagingErrorCode.ThirdPartyAuthError,
        };
        var drift = listed.Where(c => !pinned.Contains(c))
            .Concat(pinned.Where(c => !listed.Contains(c)))
            .ToList();
        Assert.Empty(drift);
    }
}
