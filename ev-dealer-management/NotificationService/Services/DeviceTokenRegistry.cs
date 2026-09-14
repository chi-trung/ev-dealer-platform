using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using Serilog;

namespace NotificationService.Services;

public interface IDeviceTokenRegistry
{
    /// <summary>
    /// Register (upsert) <paramref name="token"/> for subject <paramref name="key"/>.
    /// Idempotent: re-registering the same token refreshes UpdatedAt, a new
    /// token adds a row (multi-device), never duplicates — including under
    /// concurrent registration (the UNIQUE index + one retry absorb the race,
    /// so the endpoint never 500s on simultaneous tabs/retries). At the
    /// per-subject cap a NEW token evicts the least-recently-refreshed rows
    /// instead of being rejected (Issue #44).
    /// </summary>
    Task RegisterAsync(string key, string token, CancellationToken ct = default);

    /// <summary>
    /// All live tokens for a subject (may be empty). Fail-soft: a registry
    /// that breaks at read time logs an error and returns empty — the caller
    /// degrades to log-only instead of requeuing a healthy event.
    /// </summary>
    Task<IReadOnlyList<string>> GetTokensAsync(string key, CancellationToken ct = default);

    /// <summary>Remove one token (browser unregistration / logout).
    /// Returns false if it wasn't registered — including when a concurrent
    /// revoke removed, or a concurrent refresh moved, the row between our
    /// read and save.</summary>
    Task<bool> RevokeAsync(string key, string token, CancellationToken ct = default);
}

/// <summary>Best-effort cleanup hooks on top of the raw registry methods.</summary>
public static class DeviceTokenRegistryExtensions
{
    /// <summary>
    /// Revoke the tokens FCM reported as permanently dead (Issue #44), one
    /// <see cref="MulticastResult.DeadTokens"/> list per registry subject.
    /// NEVER throws: a delivery that already reached at least one device must
    /// not fail the event over cleanup bookkeeping — requeuing would
    /// RE-PUSH a delivered notification to every live device just because one
    /// revocation hit SQLITE_BUSY. Each failed revoke is logged and dropped;
    /// the dead row simply occupies a cap slot until the next send retries the
    /// cleanup (or an LRU eviction eventually takes it).
    /// <paramref name="key"/> is nullable so mixed-path consumers (payload
    /// token wins, registry otherwise) can pass "the subject the tokens came
    /// from, if any" without branching; a null key or empty list is a no-op.
    /// </summary>
    public static async Task RevokeDeadTokensAsync(
        this IDeviceTokenRegistry registry, string? key, IReadOnlyList<string> deadTokens,
        CancellationToken ct = default)
    {
        if (key is null || deadTokens.Count == 0) return;
        foreach (var token in deadTokens)
        {
            try
            {
                if (await registry.RevokeAsync(key, token, ct))
                    Log.Information("🧹 Revoked dead token for {Key} (FCM rejected it permanently)", key);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Dead-token revoke failed for {Key}; will retry on the next send", key);
            }
        }
    }
}

public class DeviceTokenRegistry : IDeviceTokenRegistry
{
    /// <summary>Cap per subject. FCM multicast tolerates 500 tokens, but a
    /// subject with hundreds of live tokens is spam or abuse, not one real
    /// customer with a laptop and a phone. Registration is authenticated
    /// (Issue #36) but each caller still owns live subjects — the cap bounds
    /// one token hoarding a mailbox, so it stays. Issue #44 changed what
    /// happens AT the cap: because some subjects (dealer:&lt;id&gt;) are
    /// shared by a whole staff, a hard 409 turned accumulated stale tokens
    /// into a permanent lockout — the 21st login of a real device silently
    /// got no push and never re-registered. Now a new token evicts the
    /// least-recently-refreshed rows (LRU by UpdatedAt) and always lands.</summary>
    public const int MaxTokensPerSubject = 20;

    // check-then-act races converge in practice after ONE retry (the re-read
    // sees the winner's committed row and takes the refresh path); 2 is
    // headroom for SQLITE_BUSY interleavings on the shared file.
    private const int RaceRetries = 2;

    private readonly NotificationDbContext _db;

    public DeviceTokenRegistry(NotificationDbContext db)
    {
        _db = db;
    }

    public async Task RegisterAsync(string key, string token, CancellationToken ct = default)
    {
        key = key.Trim();
        token = token.Trim();
        if (key.Length == 0 || token.Length == 0)
            throw new ArgumentException("Device token registration requires a non-empty key and token.");

        for (var attempt = 0; ; attempt++)
        {
            var isNew = false;
            try
            {
                var existing = await _db.DeviceTokens
                    .FirstOrDefaultAsync(t => t.Key == key && t.Token == token, ct);
                if (existing != null)
                {
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    // Cap enforced by eviction (Issue #44). Unlike the old
                    // first-attempt-only 409 check, this runs on retries too:
                    // a retry means the row is STILL absent (a committed
                    // same-row race takes the refresh branch above), and since
                    // a failed save rolls back its own eviction uncommitted,
                    // re-evaluating against the fresh count is what keeps the
                    // cap exact instead of overshooting by one. Evict the
                    // least-recently-refreshed rows (UpdatedAt ascending, the
                    // same order GetTokensAsync returns) until one more fits.
                    // Refresh of an EXISTING token never lands here, so an
                    // active device can always re-register even at the cap.
                    // The victim is read before this transaction opens, so
                    // UpdatedAt is a concurrency token (NotificationDbContext):
                    // if a refresh or revoke lands on a victim in between, the
                    // DELETE matches 0 rows and the retry re-picks against
                    // fresh data — a just-refreshed device is never evicted
                    // behind its own 204 (Issue #44 review).
                    var live = await _db.DeviceTokens.CountAsync(t => t.Key == key, ct);
                    if (live >= MaxTokensPerSubject)
                    {
                        var evict = await _db.DeviceTokens
                            .Where(t => t.Key == key)
                            .OrderBy(t => t.UpdatedAt)
                            .Take(live - MaxTokensPerSubject + 1)
                            .ToListAsync(ct);
                        _db.DeviceTokens.RemoveRange(evict);
                        Log.Information("♻️ Subject {Key} at device-token cap; evicted {Count} least-recently-used",
                            key, evict.Count);
                    }
                    isNew = true;
                    _db.DeviceTokens.Add(new DeviceToken { Key = key, Token = token });
                }
                await _db.SaveChangesAsync(ct);
                Log.Information("🔑 Device token registered for {Key} ({Action})", key,
                    isNew ? "new" : "refresh");
                return;
            }
            catch (Exception ex) when (attempt < RaceRetries && IsTransientRace(ex))
            {
                // Two requests raced the same (Key, Token): one INSERT lost to
                // the UNIQUE index, or an UPDATE/DELETE found 0 affected rows
                // because a concurrent revoke removed the row first. The
                // documented contract is idempotent success — discard the
                // failed change set and re-run: the second read sees the
                // winner's state and converges to a refresh.
                Log.Debug("Registry write raced on {Key} ({Error}); retrying", key, ex.GetType().Name);
                _db.ChangeTracker.Clear();
            }
        }
    }

    private static bool IsTransientRace(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => true,
        DbUpdateException { InnerException: SqliteException se } =>
            se.SqliteErrorCode is 19 /* SQLITE_CONSTRAINT (UNIQUE hit) */
                              or 5 /* SQLITE_BUSY (concurrent writer on file DB) */,
        _ => false,
    };

    public async Task<IReadOnlyList<string>> GetTokensAsync(string key, CancellationToken ct = default)
    {
        key = key.Trim();
        if (key.Length == 0) return Array.Empty<string>();
        try
        {
            return await _db.DeviceTokens
                .Where(t => t.Key == key)
                .OrderBy(t => t.UpdatedAt)
                .Select(t => t.Token)
                .ToListAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqliteException
                                       or IOException or UnauthorizedAccessException)
        {
            // Read-time fail-soft, promised by the boot-time catch in
            // Program.cs and docs/EVENTS.md: a registry that dies mid-run
            // (file corrupted, disk pulled) degrades the delivery decision to
            // log-only — pre-Issue-#33 behavior — instead of throwing inside
            // the consumer and requeueing an otherwise-healthy event.
            Log.Error(ex, "⚠️ Device token lookup failed for {Key}; degrading to log-only.", key);
            return Array.Empty<string>();
        }
    }

    public async Task<bool> RevokeAsync(string key, string token, CancellationToken ct = default)
    {
        key = key.Trim();
        token = token.Trim();
        try
        {
            var row = await _db.DeviceTokens
                .FirstOrDefaultAsync(t => t.Key == key && t.Token == token, ct);
            if (row == null) return false;
            _db.DeviceTokens.Remove(row);
            await _db.SaveChangesAsync(ct);
            Log.Information("🗑️ Device token revoked for {Key}", key);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent revoke deleted the row between our read and save.
            // The end state is exactly what both callers asked for; the loser
            // reports not-found (404) rather than a 500.
            _db.ChangeTracker.Clear();
            return false;
        }
    }
}
