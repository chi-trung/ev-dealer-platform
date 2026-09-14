using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #33: the DeviceToken registry is the out-of-band token source the
/// push consumers fall back to when the event payload carries no token.
/// These tests pin the semantics the consumers rely on:
///   - key formatting lives in exactly one place (NotificationSubjects),
///   - (Key, Token) is a dedupe: re-registering refreshes, never duplicates,
///     including when two registrations race (the UNIQUE index + retry absorb it),
///   - several tokens per key fan out (multi-device), bounded per subject —
///     since Issue #44 the cap evicts least-recently-used rows, never rejects,
///   - revoke is exact and reports whether anything was removed,
///   - the best-effort dead-token revoke helper never throws.
/// Real SQLite is required — the EF InMemory provider does not enforce the
/// unique index, so the dedupe guarantee would be untested there. Each test
/// gets its own temp DB file (deleted on dispose), which is how production
/// runs too; a shared-cache :memory: name would be kept alive by connection
/// pooling and leak rows across tests.
/// </summary>
public class DeviceTokenRegistryTests : IDisposable
{
    private readonly string _dbPath;

    public DeviceTokenRegistryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"device_token_tests_{Guid.NewGuid():N}.db");
        using var db = New();
        db.Database.EnsureCreated();
    }

    private DbContextOptions<NotificationDbContext> Options =>
        new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

    private NotificationDbContext New() => new(Options);
    private DeviceTokenRegistry NewRegistry() => new(New());

    public void Dispose()
    {
        // Release pooled connections first, then drop the file — leaving it
        // behind would litter %TEMP% once per test run (42+ files).
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    // ---- subject key formatting -------------------------------------------

    [Fact]
    public void CustomerSubject_HasExactSpellingConsumersBuildKeysWith()
    {
        // The delivery path looks tokens up by this string. A producer-side
        // or test-side format drift means "no tokens" -> log-only, i.e. the
        // silent-drop class this whole file guards against. Pin the bytes.
        Assert.Equal("customer:42", NotificationSubjects.Customer(42));
        Assert.Equal("customer:0", NotificationSubjects.Customer(0));
    }

    [Fact]
    public void UserAndDealerSubjects_HaveExactSpellingAuthChecksMatchOn()
    {
        // Issue #36: the controller's write scope compares the request key
        // against NotificationSubjects.User/Dealer output — registration and
        // every consumer (dealer fan-out lands in #38) must agree on one
        // spelling, same failure class as the customer key above.
        Assert.Equal("user:7", NotificationSubjects.User(7));
        Assert.Equal("user:0", NotificationSubjects.User(0));
        Assert.Equal("dealer:3", NotificationSubjects.Dealer(3));
    }

    // ---- register / lookup ---------------------------------------------------

    [Fact]
    public async Task Register_ThenGet_ReturnsToken()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:7", "tok-a");

        Assert.Equal(new[] { "tok-a" }, await reg.GetTokensAsync("customer:7"));
    }

    [Fact]
    public async Task Register_SameTokenTwice_DedupesAndRefreshes()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:7", "tok-a");
        DateTime first;
        await using (var db = New())
            first = (await db.DeviceTokens.SingleAsync()).UpdatedAt;

        // Rewind the stored UpdatedAt so the refresh is DETECTABLE: a no-op
        // refresh (someone dropping the assignment) must fail this assert,
        // regardless of how coarse or fine the system clock tick is.
        await using (var db = New())
        {
            var row = await db.DeviceTokens.SingleAsync();
            row.UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        await reg.RegisterAsync("customer:7", "tok-a");

        await using (var db = New())
        {
            var row = await db.DeviceTokens.SingleAsync();
            Assert.Equal(1, await db.DeviceTokens.CountAsync());
            Assert.True(row.UpdatedAt > first,
                "re-registration must refresh UpdatedAt to now");
        }
    }

    [Fact]
    public async Task Register_MultipleDevices_AllReturnedOldestFirst()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:7", "tok-old");
        await using (var db = New())
        {
            var old = await db.DeviceTokens.SingleAsync(t => t.Token == "tok-old");
            old.UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }
        await reg.RegisterAsync("customer:7", "tok-new");

        // Multicast fan-out order is oldest-device-first (stable, deterministic);
        // built with an explicit stale timestamp, not wall-clock sleep.
        Assert.Equal(new[] { "tok-old", "tok-new" }, await reg.GetTokensAsync("customer:7"));
    }

    [Fact]
    public async Task GetTokens_KeysAreIsolated()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:1", "tok-c1");
        await reg.RegisterAsync("customer:2", "tok-c2");

        Assert.Equal(new[] { "tok-c1" }, await reg.GetTokensAsync("customer:1"));
        Assert.Equal(new[] { "tok-c2" }, await reg.GetTokensAsync("customer:2"));
        Assert.Empty(await reg.GetTokensAsync("customer:3"));
    }

    [Fact]
    public async Task Register_TrimsWhitespace()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("  customer:7  ", "  tok-a  ");

        // The trimmed token is what a sender can use, and re-registering the
        // padded form must dedupe against the trimmed row, not add a second.
        Assert.Equal(new[] { "tok-a" }, await reg.GetTokensAsync("customer:7"));
        await reg.RegisterAsync("customer:7", "tok-a");
        await using (var db = New())
            Assert.Equal(1, await db.DeviceTokens.CountAsync());
    }

    [Theory]
    [InlineData("", "tok")]
    [InlineData("customer:7", "")]
    [InlineData("   ", "tok")]
    [InlineData("customer:7", "   ")]
    public async Task Register_EmptyKeyOrToken_Throws(string key, string token)
    {
        // A registration that can never be looked up must fail loudly —
        // silently storing it would resurrect the silent-drop bug class.
        await Assert.ThrowsAsync<ArgumentException>(() => NewRegistry().RegisterAsync(key, token));
    }

    [Fact]
    public async Task Register_ConcurrentSameKeyToken_NoCallerThrows()
    {
        // The check-then-act race two simultaneous tab-registrations hit:
        // loser's INSERT collides with the UNIQUE index (DbUpdateException
        // inner Sqlite 19) or the DB serializes writers (Sqlite 5 BUSY).
        // RegisterAsync must absorb both — the 500-free idempotent contract —
        // and converge to exactly one row. Collisions on a file DB with
        // parallel writers are near-certain but not guaranteed; the
        // deterministic halves of this contract are pinned by
        // UniqueKeyTokenIndex_RejectsBypassOfUpsert (loser DOES throw without
        // handling) and Register_RacesIntoRefreshBelow (retry semantics).
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                await using var db = New();
                await new DeviceTokenRegistry(db).RegisterAsync("customer:7", "tok-race");
            }))
            .ToArray();

        await Task.WhenAll(tasks); // any unabsorbed DbUpdateException faults here

        await using (var db = New())
            Assert.Equal(1, await db.DeviceTokens.CountAsync(t => t.Token == "tok-race"));
    }

    [Fact]
    public async Task Register_AtCap_EvictsLeastRecentlyUsed_AndNewTokenLands()
    {
        // Issue #44 replaced the hard 409 (which wedged shared dealer
        // subjects once stale tokens filled the mailbox) with LRU eviction:
        // at the cap, a NEW token evicts the single least-recently-refreshed
        // row in the same transaction and always lands — the cap stays a cap.
        var reg = NewRegistry();
        for (var i = 0; i < DeviceTokenRegistry.MaxTokensPerSubject; i++)
            await reg.RegisterAsync("customer:7", $"tok-{i}");

        // Deterministic LRU victim: rewind tok-0 so it is strictly oldest.
        await using (var db = New())
        {
            var stale = await db.DeviceTokens.SingleAsync(t => t.Token == "tok-0");
            stale.UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        await reg.RegisterAsync("customer:7", "tok-new"); // used to throw the limit

        var live = await reg.GetTokensAsync("customer:7");
        Assert.Equal(DeviceTokenRegistry.MaxTokensPerSubject, live.Count);
        Assert.DoesNotContain("tok-0", live);   // the stale row paid for the slot
        Assert.Contains("tok-new", live);      // and the new device is registered
    }

    [Fact]
    public async Task Register_AtCap_RefreshOfExistingToken_EvictsNothing()
    {
        // Logout-proof half of the cap story (kept from #33 and never allowed
        // to regress): re-registering a token that already exists refreshes
        // UpdatedAt and never touches the eviction branch — an active device
        // cannot be locked out, let alone evict a peer's row, while the
        // mailbox is full.
        var reg = NewRegistry();
        for (var i = 0; i < DeviceTokenRegistry.MaxTokensPerSubject; i++)
            await reg.RegisterAsync("customer:7", $"tok-{i}");
        await using (var db = New())
        {
            var stale = await db.DeviceTokens.SingleAsync(t => t.Token == "tok-0");
            stale.UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        // Refresh tok-5 while tok-0 is the standing eviction victim.
        await reg.RegisterAsync("customer:7", "tok-5");

        await using (var db = New())
        {
            Assert.Equal(DeviceTokenRegistry.MaxTokensPerSubject, await db.DeviceTokens.CountAsync());
            var stale = await db.DeviceTokens.SingleAsync(t => t.Token == "tok-0");
            Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), stale.UpdatedAt);
        }
    }

    [Fact]
    public async Task Register_PastCap_StaysAtCap_AndKeepsLatestRegistration()
    {
        // Steady state under churn (the dealer staff-rotation story): five
        // devices come and go past the cap and the mailbox never grows,
        // never locks — the newest registration is always live.
        var reg = NewRegistry();
        for (var i = 0; i < DeviceTokenRegistry.MaxTokensPerSubject + 5; i++)
            await reg.RegisterAsync("customer:7", $"tok-{i}");

        var live = await reg.GetTokensAsync("customer:7");
        Assert.Equal(DeviceTokenRegistry.MaxTokensPerSubject, live.Count);
        Assert.Contains($"tok-{DeviceTokenRegistry.MaxTokensPerSubject + 4}", live);
    }

    [Fact]
    public async Task Register_CapReEvaluatedAfterRace_RetryStillEvictsToCap()
    {
        // Eviction re-runs on EVERY attempt (the old 409 checked only the
        // first). Deterministic race: attempt 0 evicts its victim, but the
        // trap deletes that row and commits a NEW one before the save, so
        // the 0-row DELETE aborts the whole transaction (uncommitted eviction
        // rolls back) and the retry re-evaluates against 20 fresh rows.
        // A retry that skipped the cap branch would overshoot to Max+1.
        var reg = NewRegistry();
        for (var i = 0; i < DeviceTokenRegistry.MaxTokensPerSubject; i++)
            await reg.RegisterAsync("customer:7", $"tok-{i}");
        await using (var db = New())
        {
            var stale = await db.DeviceTokens.SingleAsync(t => t.Token == "tok-0");
            stale.UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync(); // attempt 0's eviction target
        }

        await using var trap = new TrapDbContext(Options, async () =>
        {
            if (_trapArmed) return; // fire exactly once, on attempt 0
            _trapArmed = true;
            await using var outsider = New();
            outsider.DeviceTokens.Remove(
                await outsider.DeviceTokens.SingleAsync(t => t.Token == "tok-0"));
            outsider.DeviceTokens.Add(new DeviceToken { Key = "customer:7", Token = "tok-x" });
            await outsider.SaveChangesAsync();
        });
        await new DeviceTokenRegistry(trap).RegisterAsync("customer:7", "tok-new");

        var live = await NewRegistry().GetTokensAsync("customer:7");
        Assert.Equal(DeviceTokenRegistry.MaxTokensPerSubject, live.Count);
        Assert.Contains("tok-new", live);
        Assert.Contains("tok-x", live); // the winner's row is not collateral damage
    }

    [Fact]
    public async Task Register_VictimRefreshedAfterVictimScan_RetrySparesItAndEvictsTrueLRU()
    {
        // Issue #44 review, the write-skew direction: the victim list is read
        // BEFORE SaveChangesAsync opens its transaction, so a refresh can land
        // on the victim in between — and the refreshed device just got its 204
        // and believes it is registered. The UpdatedAt concurrency token makes
        // the DELETE match 0 rows, aborting (and rolling back) the eviction,
        // and the retry re-scans to evict the NOW-oldest row instead. Without
        // the token, tok-0 would be silently deleted behind its own refresh.
        var reg = NewRegistry();
        for (var i = 0; i < DeviceTokenRegistry.MaxTokensPerSubject; i++)
            await reg.RegisterAsync("customer:7", $"tok-{i}");
        await using (var db = New())
        {
            // Deterministic LRU ladder: tok-0 is attempt 0's victim, tok-1 is
            // the guaranteed next-oldest for the retry (registration times
            // would otherwise tie at clock resolution).
            var stale = await db.DeviceTokens.SingleAsync(t => t.Token == "tok-0");
            stale.UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var next = await db.DeviceTokens.SingleAsync(t => t.Token == "tok-1");
            next.UpdatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync(); // attempt 0 reads tok-0 as the victim
        }

        await using var trap = new TrapDbContext(Options, async () =>
        {
            if (_trapArmed) return; // fire exactly once, between scan and save
            _trapArmed = true;
            await using var refreshingDevice = New();
            var v = await refreshingDevice.DeviceTokens.SingleAsync(t => t.Token == "tok-0");
            v.UpdatedAt = DateTime.UtcNow; // commits before our save opens
            await refreshingDevice.SaveChangesAsync();
        });
        await new DeviceTokenRegistry(trap).RegisterAsync("customer:7", "tok-new");

        var live = await NewRegistry().GetTokensAsync("customer:7");
        Assert.Equal(DeviceTokenRegistry.MaxTokensPerSubject, live.Count); // cap still exact
        Assert.Contains("tok-0", live);   // the refreshed device survived the race
        Assert.Contains("tok-new", live); // and the new token still landed
        Assert.DoesNotContain("tok-1", live); // retry evicted the true oldest
    }

    [Fact]
    public async Task GetTokens_DoesNotThrowWhenRegistryDiesMidRun()
    {
        // Fail-soft is promised to the consumers (a dead DB must not requeue
        // healthy events through retry->DLQ churn). Kill the DB file under a
        // registry, then read: expect empty + no throw, not an exception that
        // OrderCreatedConsumer.HandleAsync would propagate.
        await NewRegistry().RegisterAsync("customer:7", "tok-a");
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);

        var tokens = await NewRegistry().GetTokensAsync("customer:7");
        Assert.Empty(tokens);
    }

    // ---- revoke --------------------------------------------------------------

    [Fact]
    public async Task Revoke_RemovesExactlyOneDevice()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:7", "tok-a");
        await reg.RegisterAsync("customer:7", "tok-b");

        Assert.True(await reg.RevokeAsync("customer:7", "tok-a"));
        Assert.Equal(new[] { "tok-b" }, await reg.GetTokensAsync("customer:7"));
    }

    [Fact]
    public async Task Revoke_UnknownToken_ReturnsFalseAndKeepsRows()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:7", "tok-a");

        Assert.False(await reg.RevokeAsync("customer:7", "tok-nope"));
        Assert.False(await reg.RevokeAsync("customer:8", "tok-a"));
        Assert.Equal(new[] { "tok-a" }, await reg.GetTokensAsync("customer:7"));
    }

    [Fact]
    public async Task Revoke_ConcurrentLosers_ReturnFalseNotThrow()
    {
        // (a) Prove the hazard is real without the catch: two contexts track
        // the same row, the loser's DELETE affects 0 rows and the provider
        // throws DbUpdateConcurrencyException. RevokeAsync's catch exists for
        // exactly this; if the catch were removed this throws back into the
        // endpoint as a 500 where the contract promises 404.
        await using (var reg1 = New())
        {
            reg1.DeviceTokens.Add(new DeviceToken { Key = "customer:7", Token = "tok-a" });
            await reg1.SaveChangesAsync();
        }
        await using var c1 = New();
        await using var c2 = New();
        var row1 = await c1.DeviceTokens.SingleAsync(t => t.Token == "tok-a");
        var row2 = await c2.DeviceTokens.SingleAsync(t => t.Token == "tok-a");
        c1.DeviceTokens.Remove(row1);
        c2.DeviceTokens.Remove(row2);
        await c1.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => c2.SaveChangesAsync());

        // (b) Deterministic through the SHIPPED method: the row is committed-
        // away by a second context between RevokeAsync's read and its save,
        // so the DELETE affects 0 rows and the catch must turn it into
        // false (→ 404), not a throw (→ 500). Row re-added first: part (a)
        // emptied the table, and a missing-at-read revoke returns false
        // without ever reaching the caught save.
        await using (var seed = New())
        {
            seed.DeviceTokens.Add(new DeviceToken { Key = "customer:7", Token = "tok-a" });
            await seed.SaveChangesAsync();
        }
        await using var trap = new TrapDbContext(Options, async () =>
        {
            await using var killer = New();
            killer.DeviceTokens.RemoveRange(await killer.DeviceTokens.ToListAsync());
            await killer.SaveChangesAsync();
        });
        Assert.False(await new DeviceTokenRegistry(trap).RevokeAsync("customer:7", "tok-a"));
    }

    [Fact]
    public async Task Register_ConcurrentDuplicate_ConvergesViaCatchAndRetry()
    {
        // Deterministic loser INSERT: the same (Key, Token) row is committed
        // by a second context between RegisterAsync's read (empty) and its
        // save, so the real UNIQUE collision happens *inside* the shipped
        // code path and the retry must land on the refresh branch. Without
        // the catch+retry this throws DbUpdateException out of the "idempotent"
        // endpoint (500).
        var fresh = NewRegistry();
        await fresh.RegisterAsync("customer:7", "tok-r"); // exists, different token: cap not hit

        await using var trap = new TrapDbContext(Options, async () =>
        {
            if (_trapArmed) return; // fire exactly once, on attempt 0
            _trapArmed = true;
            await using var winner = New();
            winner.DeviceTokens.Add(new DeviceToken { Key = "customer:7", Token = "tok-t" });
            await winner.SaveChangesAsync();
        });
        var reg = new DeviceTokenRegistry(trap);
        await reg.RegisterAsync("customer:7", "tok-t"); // attempt 0 collides; retry refreshes

        await using (var db = New())
            Assert.Equal(1, await db.DeviceTokens.CountAsync(t => t.Token == "tok-t"));
    }

    private bool _trapArmed;

    /// <summary>DbContext that runs an external write just before its own
    /// SaveChangesAsync — forces check-then-act interleavings deterministically
    /// instead of racing threads and hoping.</summary>
    private sealed class TrapDbContext : NotificationDbContext
    {
        private readonly Func<Task> _beforeSave;
        public TrapDbContext(DbContextOptions<NotificationDbContext> options, Func<Task> beforeSave)
            : base(options) => _beforeSave = beforeSave;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _beforeSave();
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    // ---- schema guarantee ------------------------------------------------------

    [Fact]
    public async Task UniqueKeyTokenIndex_RejectsBypassOfUpsert()
    {
        // The registry upserts, but the (Key, Token) UNIQUE index is the real
        // dedupe guarantee (two concurrent registrations race past the
        // FirstOrDefault check). Insert directly to prove the db enforces it.
        await using (var db = New())
        {
            db.DeviceTokens.Add(new DeviceToken { Key = "customer:7", Token = "tok-a" });
            await db.SaveChangesAsync();
            db.DeviceTokens.Add(new DeviceToken { Key = "customer:7", Token = "tok-a" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    // ---- best-effort dead-token revoke (Issue #44) -----------------------------

    [Fact]
    public async Task RevokeDead_RemovesListedTokens_KeepsOthers()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:7", "tok-dead");
        await reg.RegisterAsync("customer:7", "tok-live");

        await reg.RevokeDeadTokensAsync("customer:7", new[] { "tok-dead" });

        Assert.Equal(new[] { "tok-live" }, await reg.GetTokensAsync("customer:7"));
    }

    [Fact]
    public async Task RevokeDead_NullKeyOrEmptyList_IsNoOp()
    {
        // The mixed-path consumers pass "the subject the tokens came from, if
        // any" — a payload-token send reports null and must not touch rows,
        // and a clean send reports [] and must not either.
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:7", "tok-a");

        await reg.RevokeDeadTokensAsync(null, new[] { "tok-a" });
        await reg.RevokeDeadTokensAsync("customer:7", Array.Empty<string>());

        Assert.Equal(new[] { "tok-a" }, await reg.GetTokensAsync("customer:7"));
    }

    [Fact]
    public async Task RevokeDead_NeverThrows_WhenRevokeBlowsUpMidway()
    {
        // THE contract of the helper: a delivery that already reached devices
        // must not fail the event over cleanup bookkeeping (requeueing would
        // RE-PUSH the notification). One token's revoke throws; the rest must
        // still be attempted and the call must return normally.
        var inner = NewRegistry();
        var booby = new ThrowingRegistry(inner, throwOn: "tok-booby");
        await inner.RegisterAsync("customer:7", "tok-booby");
        await inner.RegisterAsync("customer:7", "tok-dead");

        await booby.RevokeDeadTokensAsync("customer:7", new[] { "tok-booby", "tok-dead" });

        // tok-booby survived (its revoke threw, and threw-away is dropped by
        // design); tok-dead was revoked despite the earlier failure.
        Assert.Equal(new[] { "tok-booby" }, await inner.GetTokensAsync("customer:7"));
    }

    /// <summary>Registry double that throws on one specific token's revoke —
    /// stands in for SQLITE_BUSY or a dead DB file hitting exactly one row.</summary>
    private sealed class ThrowingRegistry : IDeviceTokenRegistry
    {
        private readonly IDeviceTokenRegistry _inner;
        private readonly string _throwOn;
        public ThrowingRegistry(IDeviceTokenRegistry inner, string throwOn)
            => (_inner, _throwOn) = (inner, throwOn);

        public Task RegisterAsync(string key, string token, CancellationToken ct = default)
            => _inner.RegisterAsync(key, token, ct);
        public Task<IReadOnlyList<string>> GetTokensAsync(string key, CancellationToken ct = default)
            => _inner.GetTokensAsync(key, ct);
        public Task<bool> RevokeAsync(string key, string token, CancellationToken ct = default)
            => token == _throwOn
                ? throw new InvalidOperationException("simulated db failure")
                : _inner.RevokeAsync(key, token, ct);
    }
}
