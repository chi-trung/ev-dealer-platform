using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using Serilog;

namespace NotificationService.Services;

/// <summary>Raised when a subject already holds MaxTokensPerSubject live
/// tokens and a NEW token arrives — mapped to HTTP 409 by the controller.</summary>
public class DeviceTokenLimitExceededException(string key)
    : Exception($"Token limit reached for device subject '{key}'.");

public interface IDeviceTokenRegistry
{
    /// <summary>
    /// Register (upsert) <paramref name="token"/> for subject <paramref name="key"/>.
    /// Idempotent: re-registering the same token refreshes UpdatedAt, a new
    /// token adds a row (multi-device), never duplicates — including under
    /// concurrent registration (the UNIQUE index + one retry absorb the race,
    /// so the endpoint never 500s on simultaneous tabs/retries).
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
    /// revoke removed it first.</summary>
    Task<bool> RevokeAsync(string key, string token, CancellationToken ct = default);
}

public class DeviceTokenRegistry : IDeviceTokenRegistry
{
    /// <summary>Cap per subject. FCM multicast tolerates 500 tokens, but a
    /// subject with hundreds of live tokens is spam or abuse, not one real
    /// customer with a laptop and a phone. Registration is anonymous
    /// (Issue #33), so the cap is the only backpressure.</summary>
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
                    // Cap checked only on the first attempt: a retry means a
                    // concurrent request already committed this exact row.
                    // (existing == null above already excludes the row itself.)
                    if (attempt == 0 &&
                        await _db.DeviceTokens.CountAsync(t => t.Key == key, ct) >= MaxTokensPerSubject)
                        throw new DeviceTokenLimitExceededException(key);
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
