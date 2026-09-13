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
///   - several tokens per key fan out (multi-device),
///   - revoke is exact and reports whether anything was removed.
/// Real SQLite over :memory: (shared-cache named dbs so multiple connections
/// see the same data) — the unique index under test does not exist in the
/// EF InMemory provider.
/// </summary>
public class DeviceTokenRegistryTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<NotificationDbContext> _options;

    public DeviceTokenRegistryTests()
    {
        // A shared-cache in-memory db survives as long as one connection is
        // open; disposing this test's connection drops the data, so every
        // test gets a clean database. The name is per-instance because xunit
        // runs test classes in parallel and a pooled handle would let one
        // leak into the next.
        _conn = new SqliteConnection($"DataSource=device_token_tests_{Guid.NewGuid():N};Cache=Shared");
        _conn.Open();
        _options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite(_conn)
            .Options;
        new NotificationDbContext(_options).Database.EnsureCreated();
    }

    private NotificationDbContext New() => new(_options);
    private DeviceTokenRegistry NewRegistry() => new(New());

    public void Dispose() => _conn.Dispose();

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
        await Task.Delay(15); // UpdatedAt = DateTime.UtcNow; ensure it advances
        await reg.RegisterAsync("customer:7", "tok-a");

        Assert.Equal(new[] { "tok-a" }, await reg.GetTokensAsync("customer:7"));
        await using (var db = New())
        {
            var row = await db.DeviceTokens.SingleAsync();
            Assert.True((DateTime.UtcNow - row.UpdatedAt) < TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Register_MultipleDevices_AllReturnedOldestFirst()
    {
        var reg = NewRegistry();
        await reg.RegisterAsync("customer:7", "tok-old");
        await Task.Delay(15);
        await reg.RegisterAsync("customer:7", "tok-new");

        // Multicast fan-out order is oldest-device-first (stable, deterministic).
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
