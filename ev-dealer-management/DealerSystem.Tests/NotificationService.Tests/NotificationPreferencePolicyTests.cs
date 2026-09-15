using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #56: NotificationPreferencePolicy is the read-side of #51's stored
/// documents — the code that decides, at a user:&lt;id&gt; fan-out point,
/// whether a push may be delivered. These tests pin the three decisions the
/// issue delegated and demanded be "decided and tested":
///
///  1. the type-tag → flag mapping (every row, muted AND unmuted — the
///     acceptance pair: a muted-type event is NOT delivered, an unmuted one
///     is), pinned through ShouldDeliverAsync behavior, not the private
///     dictionary;
///  2. scope: customer:/dealer: subjects are never filtered, even when a
///     document exists under their key (the id-space-collision guard — a
///     row at "customer:5" must not be able to mute anything);
///  3. failure mode: FAIL OPEN. A store outage delivers (loud log) rather
///     than mass-suppressing every user push; an unmapped type tag also
///     delivers (unmapped ≠ muted), while a muted in-app channel suppresses
///     everything, mapped or not.
///
/// Real store (temp-file SQLite) so the GetAsync defaults contract — never
/// saved ⇒ shared defaults — is exercised end-to-end, not mocked away.
/// </summary>
public class NotificationPreferencePolicyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<NotificationDbContext> _options;

    public NotificationPreferencePolicyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pref_policy_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var db = new NotificationDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private NotificationPreferencesStore Store() => new(new NotificationDbContext(_options));
    private NotificationPreferencePolicy Policy() => new(Store());

    /// <summary>Defaults match NotificationPreferencesDefaults (email on, sms
    /// off, in-app on, orders/deliveries/payments on, system/promotions off).</summary>
    private static NotificationPreferencesDto Prefs(
        bool inApp = true, bool orders = true, bool deliveries = true,
        bool payments = true, bool system = false, bool promotions = false) => new()
        {
            EmailNotifications = true,
            SmsNotifications = false,
            InAppNotifications = inApp,
            Orders = orders,
            Deliveries = deliveries,
            Payments = payments,
            SystemAlerts = system,
            Promotions = promotions,
        };

    /// <summary>Every flag ON except the named one — isolates a single
    /// mapping row so a wrong mapping can't hide behind another true flag.</summary>
    private static NotificationPreferencesDto AllOnExcept(string flag) => new()
    {
        EmailNotifications = true,
        SmsNotifications = true,
        InAppNotifications = true,
        Orders = flag != "Orders",
        Deliveries = flag != "Deliveries",
        Payments = flag != "Payments",
        SystemAlerts = flag != "System",
        Promotions = flag != "Promotions",
    };

    /// <summary>Every consumer data["type"] tag and the flag it must be
    /// gated by — the table README.md documents, pinned by behavior.</summary>
    public static IEnumerable<object[]> TagToFlag => new[]
    {
        new object[] { "quote", "Orders" },
        new object[] { "order", "Orders" },
        new object[] { "orders", "Orders" },   // VehicleReserved's plural spelling
        new object[] { "sale", "Orders" },
        new object[] { "contract", "Orders" },
        new object[] { "orderStatus", "Deliveries" },
        new object[] { "payment", "Payments" },
        new object[] { "customer", "System" },
        new object[] { "testdrive", "System" },
        new object[] { "vehicleCreated", "System" },
        new object[] { "vehicleUpdated", "System" },
        new object[] { "vehicleDeleted", "System" },
        new object[] { "promotion", "Promotions" },
        new object[] { "promotions", "Promotions" },
    };

    // ---- mapping rows: the acceptance pair (muted → NOT delivered, on → delivered) ----

    [Theory]
    [MemberData(nameof(TagToFlag))]
    public async Task MutedFlag_SuppressesDelivery(string tag, string flag)
    {
        await Store().PutAsync("user:1", AllOnExcept(flag));

        Assert.False(await Policy().ShouldDeliverAsync("user:1", tag));
    }

    [Theory]
    [MemberData(nameof(TagToFlag))]
    public async Task UnmutedFlag_Delivers(string tag, string flag)
    {
        // The same document as above but with the mapped flag left ON:
        // every OTHER flag is off-ish (system/promotions default) so this
        // proves the row keys off exactly its mapped flag.
        await Store().PutAsync("user:1", new NotificationPreferencesDto
        {
            EmailNotifications = true,
            SmsNotifications = false,
            InAppNotifications = true,
            Orders = flag == "Orders",
            Deliveries = flag == "Deliveries",
            Payments = flag == "Payments",
            SystemAlerts = flag == "System",
            Promotions = flag == "Promotions",
        });

        Assert.True(await Policy().ShouldDeliverAsync("user:1", tag));
    }

    [Fact]
    public async Task TagLookup_IsCaseInsensitive()
    {
        // The dictionary is OrdinalIgnoreCase so a producer tagging "QUOTE"
        // can't dodge a muted Orders flag by spelling — and the same holds
        // for the camelCase tags (orderStatus is mapped, not re-found by
        // some accidental case-normalizing elsewhere).
        await Store().PutAsync("user:1", AllOnExcept("Orders"));

        Assert.False(await Policy().ShouldDeliverAsync("user:1", "QUOTE"));
        Assert.False(await Policy().ShouldDeliverAsync("user:1", "Quote"));

        await Store().PutAsync("user:1", AllOnExcept("Deliveries"));

        Assert.False(await Policy().ShouldDeliverAsync("user:1", "orderstatus"));
        Assert.False(await Policy().ShouldDeliverAsync("user:1", "ORDERSTATUS"));
    }

    // ---- channel gate: in-app is the one FCM exercises ----

    [Fact]
    public async Task InAppChannelMuted_SuppressesRegardlessOfType()
    {
        await Store().PutAsync("user:1", Prefs(inApp: false, orders: true, system: true, promotions: true));

        Assert.False(await Policy().ShouldDeliverAsync("user:1", "quote"));
        Assert.False(await Policy().ShouldDeliverAsync("user:1", "payment"));
    }

    [Fact]
    public async Task InAppChannelMuted_AlsoSuppressesUnknownTag_ChannelGateRunsFirst()
    {
        // Fail-open on unknown tags is about the TYPE table; a muted channel
        // is a decision the user made and must win.
        await Store().PutAsync("user:1", Prefs(inApp: false));

        Assert.False(await Policy().ShouldDeliverAsync("user:1", "notARealTag"));
    }

    [Fact]
    public async Task EmailAndSmsFlags_StayInert_FcmPushIgnoresThem()
    {
        // The frontend hides those two toggles (#56 acceptance: UI and
        // enforcement match). This pins WHY: email=false alone must not
        // suppress the push — FCM is the in-app channel, not email.
        await Store().PutAsync("user:1", new NotificationPreferencesDto
        {
            EmailNotifications = false,
            SmsNotifications = false,
            InAppNotifications = true,
            Orders = true, Deliveries = true, Payments = true,
        });

        Assert.True(await Policy().ShouldDeliverAsync("user:1", "quote"));
    }

    // ---- scope: only user:<n> has a preferences surface ----

    [Theory]
    [InlineData("customer:1")]
    [InlineData("dealer:1")]
    public async Task NonUserSubject_NeverFiltered_EvenWithADocumentAtItsKey(string subject)
    {
        // The id-space-collision guard: an all-muted row exists under this
        // exact key, and delivery still happens. If the policy ever starts
        // reading non-user keys, this flips and fails loudly.
        await Store().PutAsync(subject, AllOnExcept("__none__") with { InAppNotifications = false });

        Assert.True(await Policy().ShouldDeliverAsync(subject, "quote"));
        Assert.True(await Policy().ShouldDeliverAsync(subject, "payment"));
    }

    [Theory]
    [InlineData("user:0")]
    [InlineData("user:-3")]
    [InlineData("user:abc")]
    [InlineData("customer:user:1")]
    public async Task MalformedUserSubjects_AreNotPreferenceSurfaces_Deliver(string subject)
    {
        // IsUserSubject mirrors the registration-auth compare (positive int
        // suffix); anything else is not a preferences surface.
        Assert.True(await Policy().ShouldDeliverAsync(subject, "quote"));
    }

    // ---- never-saved: the GetAsync defaults contract ----

    [Theory]
    [InlineData("quote", true)]        // Orders default on
    [InlineData("orderStatus", true)]  // Deliveries default on
    [InlineData("payment", true)]      // Payments default on
    [InlineData("customer", false)]    // SystemAlerts default OFF
    [InlineData("testdrive", false)]   // SystemAlerts default OFF
    [InlineData("promotion", false)]   // Promotions default OFF
    public async Task NeverSavedUser_GetsSharedDefaults(string tag, bool expected)
    {
        // user:999 has no row. The defaults are the same NotificationPreferencesDefaults
        // #51's GET returns, so "saved nothing" and "GET" agree.
        Assert.Equal(expected, await Policy().ShouldDeliverAsync("user:999", tag));
    }

    // ---- failure mode: fail open, loudly ----

    [Fact]
    public async Task StoreOutage_Delivers_FailOpen()
    {
        // #56's decision: mass-suppression during an outage is the
        // silent-drop class docs/EVENTS.md exists to prevent; delivering a
        // muted-by-preference push is visible and recoverable.
        var policy = new NotificationPreferencePolicy(new ThrowingStore());

        Assert.True(await policy.ShouldDeliverAsync("user:1", "quote"));
    }

    [Fact]
    public async Task UnknownTag_Delivers_UnmappedIsNotMuted()
    {
        // A new consumer shipping a new tag must not be muted-by-absence
        // before the table learns about it — even for a user with everything
        // else muted except an unrelated flag.
        await Store().PutAsync("user:1", Prefs(orders: false));

        Assert.True(await Policy().ShouldDeliverAsync("user:1", "someFutureTag"));
    }

    /// <summary>Every read throws — emulates the preferences-store outage
    /// (db file locked/corrupt) the fail-open path exists for.</summary>
    private sealed class ThrowingStore : INotificationPreferencesStore
    {
        public Task<NotificationPreferencesDto> GetAsync(string key, CancellationToken ct = default)
            => throw new IOException($"simulated outage for {key}");

        public Task<NotificationPreferencesDto> PutAsync(string key, NotificationPreferencesDto prefs, CancellationToken ct = default)
            => throw new IOException("simulated outage");
    }
}
