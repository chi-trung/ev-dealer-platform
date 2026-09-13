using NotificationService.Data;
using NotificationService.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace NotificationService.Services;

public interface IDeviceTokenRegistry
{
    /// <summary>
    /// Register (upsert) <paramref name="token"/> for subject <paramref name="key"/>.
    /// Idempotent: re-registering the same token refreshes UpdatedAt, a new
    /// token adds a row (multi-device), never duplicates.
    /// </summary>
    Task RegisterAsync(string key, string token, CancellationToken ct = default);

    /// <summary>All live tokens for a subject (may be empty).</summary>
    Task<IReadOnlyList<string>> GetTokensAsync(string key, CancellationToken ct = default);

    /// <summary>Remove one token (browser unregistration / logout).</summary>
    Task<bool> RevokeAsync(string key, string token, CancellationToken ct = default);
}

public class DeviceTokenRegistry : IDeviceTokenRegistry
{
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

        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(t => t.Key == key && t.Token == token, ct);
        if (existing != null)
        {
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.DeviceTokens.Add(new DeviceToken { Key = key, Token = token });
        }
        await _db.SaveChangesAsync(ct);
        Log.Information("🔑 Device token registered for {Key} ({Action})", key,
            existing != null ? "refresh" : "new");
    }

    public async Task<IReadOnlyList<string>> GetTokensAsync(string key, CancellationToken ct = default)
    {
        key = key.Trim();
        if (key.Length == 0) return Array.Empty<string>();
        return await _db.DeviceTokens
            .Where(t => t.Key == key)
            .OrderBy(t => t.UpdatedAt)
            .Select(t => t.Token)
            .ToListAsync(ct);
    }

    public async Task<bool> RevokeAsync(string key, string token, CancellationToken ct = default)
    {
        key = key.Trim();
        token = token.Trim();
        var row = await _db.DeviceTokens
            .FirstOrDefaultAsync(t => t.Key == key && t.Token == token, ct);
        if (row == null) return false;
        _db.DeviceTokens.Remove(row);
        await _db.SaveChangesAsync(ct);
        Log.Information("🗑️ Device token revoked for {Key}", key);
        return true;
    }
}
