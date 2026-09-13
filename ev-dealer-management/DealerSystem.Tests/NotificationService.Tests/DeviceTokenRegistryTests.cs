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
///   - several tokens per key fan out (multi-device), bounded per subject,
///   - revoke is exact and reports whether anything was removed.
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
    public async Task Register_BeyondCapForSubject_ThrowsLimit()
    {
        var reg = NewRegistry();
        for (var i = 0; i < DeviceTokenRegistry.MaxTokensPerSubject; i++)
            await reg.RegisterAsync("customer:7", $"tok-{i}");

        var ex = await Assert.ThrowsAsync<DeviceTokenLimitExceededException>(
            () => reg.RegisterAsync("customer:7", "tok-over-limit"));
        Assert.Contains("customer:7", ex.Message);

        // Over-limit never half-writes, and refreshing an EXISTING token is
        // still allowed at the cap (logout-proof: same device re-permits).
        await using (var db = New())
            Assert.Equal(DeviceTokenRegistry.MaxTokensPerSubject, await db.DeviceTokens.CountAsync());
        await reg.RegisterAsync("customer:7", "tok-0");
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
}
