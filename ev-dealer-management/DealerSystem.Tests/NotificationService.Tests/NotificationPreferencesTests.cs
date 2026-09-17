using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Controllers;
using NotificationService.Data;
using NotificationService.Services;
using Xunit;
using Registry = NotificationService.Services.DeviceTokenRegistry;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #51: the preferences page's "saved" message stops being a lie —
/// GET/PUT /api/Notification/preferences persist per-subject documents
/// server-side. Two layers are pinned here:
///
///  - the store (real SQLite temp file, same fixture style as the #33
///    registry tests — InMemory would not enforce the UNIQUE key index that
///    the concurrent-first-save race depends on),
///  - the controller's authorization + validation decision table, unit-tested
///    directly with a synthetic ClaimsPrincipal against an in-memory store
///    fake (the [Authorize] 401 layer itself is framework behavior, verified
///    live, exactly as DeviceTokensAuthTests frames it).
///
/// The mutation-relevant guards each get a row that ONLY they catch: a PUT
/// missing one flag must 400 (not store false = muted), and a caller whose
/// "id" claim is missing/zero/garbage must 403 (no subject is built).
/// </summary>
[Collection("sqlite")]
public class NotificationPreferencesTests : IDisposable
{
    private readonly string _dbPath;

    public NotificationPreferencesTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"notif_prefs_tests_{Guid.NewGuid():N}.db");
        using var db = New();
        db.Database.EnsureCreated();
    }

    private DbContextOptions<NotificationDbContext> Options =>
        new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

    private NotificationDbContext New() => new(Options);
    private NotificationPreferencesStore NewStore() => new(New());

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    // ---- store: roundtrip, defaults, races ---------------------------------

    [Fact]
    public async Task Get_NeverSavedSubject_ReturnsSharedDefaults()
    {
        var p = await NewStore().GetAsync("user:99");
        Assert.Equal(NotificationPreferencesDefaults.Value, p);
        // Pin the values too — "identical to the frontend defaults" is the
        // contract; if someone edits one side, this names the lie.
        Assert.True(p.EmailNotifications);
        Assert.False(p.SmsNotifications);
        Assert.True(p.InAppNotifications);
        Assert.True(p.Orders);
        Assert.True(p.Deliveries);
        Assert.True(p.Payments);
        Assert.False(p.SystemAlerts);
        Assert.False(p.Promotions);
    }

    [Fact]
    public async Task Put_ThenGet_RoundTripsAllEightFlags()
    {
        var all = new NotificationPreferencesDto
        {
            EmailNotifications = false, SmsNotifications = true, InAppNotifications = false,
            Orders = false, Deliveries = true, Payments = false, SystemAlerts = true, Promotions = true,
        };
        var saved = await NewStore().PutAsync("user:1", all);
        Assert.Equal(all, saved);

        var got = await NewStore().GetAsync("user:1");
        Assert.Equal(all, got);
    }

    [Fact]
    public async Task Put_Twice_UpsertsOneRowLastWriterWins()
    {
        var store = NewStore();
        await store.PutAsync("user:2", NotificationPreferencesDefaults.Value with { SmsNotifications = true });
        var second = NotificationPreferencesDefaults.Value with { Promotions = true };
        await store.PutAsync("user:2", second);

        Assert.Equal(second, await store.GetAsync("user:2"));
        await using (var db = New())
            Assert.Equal(1, await db.NotificationPreferences.CountAsync(p => p.Key == "user:2"));
    }

    [Fact]
    public async Task DifferentKeys_DoNotShareRows()
    {
        var store = NewStore();
        await store.PutAsync("user:3", NotificationPreferencesDefaults.Value with { Orders = false });
        await store.PutAsync("user:4", NotificationPreferencesDefaults.Value);

        Assert.False((await store.GetAsync("user:3")).Orders);
        Assert.True((await store.GetAsync("user:4")).Orders);
    }

    [Fact]
    public async Task ConcurrentFirstSaves_SameKey_NoThrowLastWins()
    {
        // The UNIQUE(Key) loser must retry into the UPDATE path — the store
        //'s documented contract is last-writer-wins, never a 500. Eight
        // contenders (the #33 Register_ConcurrentSameKeyToken precedent) so
        // several SELECTs land before any INSERT commits and the
        // SQLITE_CONSTRAINT arm of IsTransientRace is near-certain;
        // SQLITE_BUSY itself rarely surfaces (Microsoft.Data.Sqlite's 30s
        // command timeout sets sqlite3_busy_timeout and absorbs waits).
        var a = NotificationPreferencesDefaults.Value with { Promotions = true };
        var b = NotificationPreferencesDefaults.Value with { Promotions = false, SystemAlerts = true };
        var tasks = Enumerable.Range(0, 8).Select(i =>
            Task.Run(async () => await NewStore().PutAsync("user:5", i % 2 == 0 ? a : b))).ToArray();
        await Task.WhenAll(tasks);

        var got = await NewStore().GetAsync("user:5");
        Assert.True(got == a || got == b);
        await using (var db = New())
            Assert.Equal(1, await db.NotificationPreferences.CountAsync(p => p.Key == "user:5"));
    }

    // ---- provider-neutral race detection (Issue #91) -----------------------

    // The SQLite arm keeps working (ConcurrentFirstSaves above covers it), but
    // the defect #91 fixed is invisible to that test: IsTransientRace used to
    // match `DbUpdateException { InnerException: SqliteException }` by TYPE, so
    // on postgres a 23505 never matched and the race retry was dead code —
    // every concurrent first-save became a 500. These pin the new detection
    // (SQLite code 19, Postgres 23505, and the message-based fallback) so a
    // future re-typing cannot silently re-break one provider while the other
    // stays green. They are unit tests over the shared helper, not DB tests,
    // because the failure mode lives in the exception-matching, not the query;
    // a mutation of the helper to its pre-#91 typed form is what makes them
    // fail (verified 2026-09-17: both Postgres cases go red, the SQLite and
    // BUSY ones stay green — the defect is pinned to the provider that lost
    // detection, not to the one that never had a problem).
    [Fact]
    public void RaceDetection_UniqueViolation_Sqlite_IsAbsorbed()
    {
        var inner = new Microsoft.Data.Sqlite.SqliteException(
            "SQLite Error 19: 'UNIQUE constraint failed: NotificationPreferences.Key'.", 19, 19);
        var ex = new DbUpdateException("An error occurred while saving.", inner);

        Assert.True(Registry.IsUniqueViolationForStore(ex));
        Assert.True(Registry.IsSqliteBusy(ex) is false);
    }

    [Fact]
    public void RaceDetection_Busy_Sqlite_IsAbsorbedButNotTreatedAsUnique()
    {
        // SQLITE_BUSY (5) is a file-DB artifact with no Postgres equivalent;
        // it is retried by its own arm and must not be folded into the
        // unique-violation one (the retry comment in PutAsync distinguishes
        // them, and the eviction path in the registry treats them differently).
        var inner = new Microsoft.Data.Sqlite.SqliteException("database is locked", 5, 5);
        var ex = new DbUpdateException("An error occurred while saving.", inner);

        Assert.True(Registry.IsSqliteBusy(ex));
        Assert.False(Registry.IsUniqueViolationForStore(ex));
    }

    [Fact]
    public void RaceDetection_UniqueViolation_Postgres_23505_IsAbsorbed()
    {
        // The actual #91 defect: Npgsql surfaces a unique violation as
        // PostgresException with SqlState 23505. Simulated without a live
        // server because the detection matches by exception shape, not by a
        // real connection.
        var inner = SimulatedPostgresException(
            "23505: duplicate key value violates unique constraint \"IX_NotificationPreferences_Key\"");
        var ex = new DbUpdateException("An error occurred while saving.", inner);

        Assert.True(Registry.IsUniqueViolationForStore(ex));
        Assert.False(Registry.IsSqliteBusy(ex));
    }

    [Fact]
    public void RaceDetection_PostgresMessageFallback_IsAbsorbed()
    {
        // Compatibility fallback for a missing SQLSTATE and English message.
        // Real PostgreSQL messages can be localized; SQLSTATE is authoritative.
        var inner = SimulatedPostgresException("duplicate key value violates unique constraint \"IX_NotificationPreferences_Key\"", sqlState: null);
        var ex = new DbUpdateException("An error occurred while saving.", inner);

        Assert.True(Registry.IsUniqueViolationForStore(ex));
    }

    [Fact]
    public void RaceDetection_RealPostgresLocalizedMessage_UsesSqlState()
    {
        var inner = new Npgsql.PostgresException("valeur dupliquée", "ERROR", "ERROR", "23505");
        Assert.True(Registry.IsUniqueViolationForStore(new DbUpdateException("save failed", inner)));
    }

    [Fact]
    public void RaceDetection_RealPostgresOtherState_OverridesMessage()
    {
        var inner = new Npgsql.PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR", "23503");
        Assert.False(Registry.IsUniqueViolationForStore(new DbUpdateException("save failed", inner)));
    }

    [Fact]
    public void RaceDetection_UnrelatedFailure_IsNotAbsorbed()
    {
        // A genuine write failure (FK, disk, NOT NULL on a non-unique column)
        // must NOT be swallowed as a race — retrying it 6 times then throwing
        // is worse than throwing once, and would mask a real bug as flake.
        var inner = new InvalidOperationException("some other failure");
        var ex = new DbUpdateException("An error occurred while saving.", inner);

        Assert.False(Registry.IsUniqueViolationForStore(ex));
        Assert.False(Registry.IsSqliteBusy(ex));
    }

    /// <summary>Builds a stand-in for Npgsql.PostgresException. The detection
    /// matches by namespace prefix ("Npgsql.") plus the SqlState property or
    /// the message; a subclass in that namespace reproduces both shapes
    /// without a live server (Docker Hub is unreachable from the dev sandbox,
    /// so the real type gets its first live check on Render).</summary>
    private static Exception SimulatedPostgresException(string message, string? sqlState = "23505")
        => new Npgsql.PostgresExceptionFake(message, sqlState);

    // ---- controller: authorization decision table --------------------------

    private sealed class FakeStore : INotificationPreferencesStore
    {
        public NotificationPreferencesDto? LastPut;
        public string? LastPutKey;
        public string? LastGetKey;
        public NotificationPreferencesDto Current = NotificationPreferencesDefaults.Value;

        public Task<NotificationPreferencesDto> GetAsync(string key, CancellationToken ct = default)
        {
            LastGetKey = key; // review-found gap: reads must use the claim-built
            // key too — LastPutKey alone only pinned the write path.
            return Task.FromResult(Current);
        }

        public Task<NotificationPreferencesDto> PutAsync(string key, NotificationPreferencesDto prefs, CancellationToken ct = default)
        {
            LastPutKey = key; LastPut = prefs; Current = prefs;
            return Task.FromResult(prefs);
        }
    }

    private static NotificationController ControllerWith(
        FakeStore store, params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "Test");
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new NotificationController(fcmService: null!, preferencesStore: store)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static NotificationController.PreferencesWireRequest FullWire(bool v = true) => new(
        EmailNotifications: v, SmsNotifications: !v, InAppNotifications: v,
        Types: new NotificationController.TypesWireRequest(v, !v, v, !v, v));

    [Theory]
    [InlineData(null)]      // no "id" claim at all
    [InlineData("")]        // empty claim
    [InlineData("abc")]     // garbage claim — int.TryParse rejects
    [InlineData("0")]       // zero/negative are not real UserService ids
    [InlineData("-7")]
    public async Task MissingOrGarbageIdClaim_BothVerbs_FourOhThree(string? idClaim)
    {
        var claims = idClaim is null ? Array.Empty<(string, string)>() : new[] { ("id", idClaim) };
        var c = ControllerWith(new FakeStore(), claims);

        Assert.IsType<ObjectResult>(await c.GetPreferences(default));
        var put = await c.PutPreferences(FullWire(), default);
        Assert.IsType<ObjectResult>(put);
        Assert.Equal(403, ((ObjectResult)put).StatusCode);
    }

    [Fact]
    public async Task Put_StoresSubjectBuiltFromClaim_NotAnythingTheClientSent()
    {
        var store = new FakeStore();
        // A caller with dealer claims gets a user subject regardless —
        // there is no dealer preferences surface to spoof toward.
        var c = ControllerWith(store, ("id", "7"), ("dealer", "3"));
        var res = await c.PutPreferences(FullWire(), default);
        Assert.IsType<OkObjectResult>(res);
        Assert.Equal("user:7", store.LastPutKey);
    }

    [Fact]
    public async Task Get_ReturnsStoreDocument_InFrontendWireShape()
    {
        var prefs = NotificationPreferencesDefaults.Value with { Deliveries = false, SystemAlerts = true };
        var store = new FakeStore { Current = prefs };
        var c = ControllerWith(store, ("id", "7"));

        var res = Assert.IsType<OkObjectResult>(await c.GetPreferences(default));
        Assert.Equal("user:7", store.LastGetKey);
        // Pin the exact anonymous-shape JSON roundtrip: axios hands the page
        // response.data, and the page reads data.notificationTypes.deliveries.
        var json = System.Text.Json.JsonSerializer.Serialize(res.Value!);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("data").GetProperty("notificationTypes").GetProperty("deliveries").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("emailNotifications").GetBoolean());
        // System=true against the defaults' all-false system+promotions pins
        // the Shape() DTO->wire mapping (a System<->Promotions column swap
        // could otherwise pass unnoticed).
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("notificationTypes").GetProperty("system").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("data").GetProperty("notificationTypes").GetProperty("promotions").GetBoolean());
    }

    // ---- controller: validation (the mute-by-omission guard) ---------------

    [Fact]
    public async Task Put_MissingOneFlag_FourHundred_NotStored()
    {
        var store = new FakeStore();
        var c = ControllerWith(store, ("id", "7"));

        // notificationTypes.deliveries omitted — everything else present.
        // MUST deserialize with Web defaults (case-INsensitive, what MVC
        // sends on the wire): under the case-sensitive default options every
        // camelCase key binds to null and the guard trips on its FIRST arm,
        // so this would pass even with the deliveries check deleted (a
        // vacuous pin — review finding on commit 805bfbe).
        var body = """
            {"emailNotifications":true,"smsNotifications":false,"inAppNotifications":true,
             "notificationTypes":{"orders":true,"payments":true,"system":false,"promotions":false}}
            """;
        var res = await c.PutPreferences(
            System.Text.Json.JsonSerializer
                .Deserialize<NotificationController.PreferencesWireRequest>(
                    body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            default);

        Assert.IsType<BadRequestObjectResult>(res);
        Assert.Null(store.LastPut); // rejected BEFORE the store was touched
    }

    [Fact]
    public async Task Put_AllEightFlagsAsWireJson_StoredAndEchoed()
    {
        // Positive control for the test above: the SAME camelCase document
        // with all eight flags present binds cleanly and is stored — proving
        // the 400 in Put_MissingOneFlag fires on the deliveries arm
        // specifically, not on deserialization artifacts.
        var store = new FakeStore();
        var c = ControllerWith(store, ("id", "7"));
        var body = """
            {"emailNotifications":true,"smsNotifications":false,"inAppNotifications":true,
             "notificationTypes":{"orders":true,"deliveries":false,"payments":true,"system":false,"promotions":true}}
            """;
        var req = System.Text.Json.JsonSerializer
            .Deserialize<NotificationController.PreferencesWireRequest>(
                body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.IsType<OkObjectResult>(await c.PutPreferences(req!, default));
        Assert.Equal("user:7", store.LastPutKey);
        Assert.NotNull(store.LastPut);
        Assert.True(store.LastPut!.EmailNotifications);
        Assert.False(store.LastPut.SmsNotifications);
        Assert.True(store.LastPut.InAppNotifications);
        Assert.True(store.LastPut.Orders);
        Assert.False(store.LastPut.Deliveries);
        Assert.True(store.LastPut.Payments);
        Assert.False(store.LastPut.SystemAlerts);
        Assert.True(store.LastPut.Promotions);
    }

    [Fact]
    public async Task Put_NullNestedTypes_FourHundred()
    {
        // "notificationTypes": null explicitly replaces the record's ctor
        // result with null (the #49 finding on array/collection shapes); the
        // recursive null-check must catch it.
        var c = ControllerWith(new FakeStore(), ("id", "7"));
        var req = new NotificationController.PreferencesWireRequest(true, false, true, null);
        Assert.IsType<BadRequestObjectResult>(await c.PutPreferences(req, default));
    }

    [Fact]
    public async Task Put_NullBody_FourHundred()
    {
        // Wire "{}" binds a non-null request with all-null flags → 400 by the
        // same guard; literal JSON "null" gives a null parameter → explicit
        // branch. Both are the #50 lesson (500-not-400 shapes) pre-empted.
        var c = ControllerWith(new FakeStore(), ("id", "7"));
        Assert.IsType<BadRequestObjectResult>(await c.PutPreferences(null, default));
    }

    [Fact]
    public async Task Put_FullDocument_RoundTripsThroughStoreIntoResponse()
    {
        var store = new FakeStore();
        var c = ControllerWith(store, ("id", "7"));
        var req = FullWire();

        var res = Assert.IsType<OkObjectResult>(await c.PutPreferences(req, default));
        Assert.NotNull(store.LastPut);
        Assert.True(store.LastPut!.EmailNotifications); // FullWire(true) — email flag stored as sent
        var json = System.Text.Json.JsonSerializer.Serialize(res.Value!);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Notification preferences updated successfully",
            doc.RootElement.GetProperty("message").GetString());
    }
}
