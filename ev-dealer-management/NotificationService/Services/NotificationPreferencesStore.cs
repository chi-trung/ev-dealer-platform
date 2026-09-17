using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using Serilog;

namespace NotificationService.Services;

/// <summary>The eight flags a subject has chosen (Issue #51).</summary>
public record NotificationPreferencesDto
{
    public bool EmailNotifications { get; init; }
    public bool SmsNotifications { get; init; }
    public bool InAppNotifications { get; init; }
    public bool Orders { get; init; }
    public bool Deliveries { get; init; }
    public bool Payments { get; init; }
    public bool SystemAlerts { get; init; }
    public bool Promotions { get; init; }
}

/// <summary>Contract for the preferences store (Issue #51). The PUT wire DTO
/// itself lives on the controller, next to the validation that explains it:
/// every field is nullable so "missing" is distinguishable from "false", and
/// these flags mean MUTE when off, so an absent field must 400 — never
/// silently default to false (the Issue #50 lesson: System.Text.Json binds
/// missing members to defaults despite non-nullable annotations). The page
/// always sends all eight, so requiring them costs nothing.</summary>
public interface INotificationPreferencesStore
{
    /// <summary>The subject's saved choices, or the shared defaults when the
    /// subject has never saved (the honest "you haven't chosen" answer — the
    /// page renders defaults today anyway).</summary>
    Task<NotificationPreferencesDto> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Upsert the full document for one subject. Race-safe: two
    /// simultaneous first-writes on the same key never surface a 500 (the
    /// UNIQUE index loser retries into the UPDATE path).</summary>
    Task<NotificationPreferencesDto> PutAsync(string key, NotificationPreferencesDto prefs, CancellationToken ct = default);
}

/// <summary>Defaults MUST stay byte-identical to the frontend's initial
/// state in NotificationPreferences.jsx — the point of Issue #51 was that
/// the page's defaults were fictional; server and page agreeing on them is
/// what makes "saved" and "unsaved" render the same truth.</summary>
public static class NotificationPreferencesDefaults
{
    public static readonly NotificationPreferencesDto Value = new()
    {
        EmailNotifications = true,
        SmsNotifications = false,
        InAppNotifications = true,
        Orders = true,
        Deliveries = true,
        Payments = true,
        SystemAlerts = false,
        Promotions = false,
    };
}

public class NotificationPreferencesStore : INotificationPreferencesStore
{
    // Same collision budget and jittered-backoff shape as the #33/#44
    // registry (see DeviceTokenRegistry.RaceRetries for the flake math);
    // one row per key means the loser's path is always the plain UPDATE.
    private const int RaceRetries = 6;

    private readonly NotificationDbContext _db;

    public NotificationPreferencesStore(NotificationDbContext db)
    {
        _db = db;
    }

    public async Task<NotificationPreferencesDto> GetAsync(string key, CancellationToken ct = default)
    {
        var row = await _db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key, ct);
        return row is null ? NotificationPreferencesDefaults.Value : ToDto(row);
    }

    public async Task<NotificationPreferencesDto> PutAsync(string key, NotificationPreferencesDto prefs, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var row = await _db.NotificationPreferences
                    .FirstOrDefaultAsync(p => p.Key == key, ct);
                if (row is null)
                {
                    _db.NotificationPreferences.Add(new Models.NotificationPreferences
                    {
                        Key = key,
                        EmailNotifications = prefs.EmailNotifications,
                        SmsNotifications = prefs.SmsNotifications,
                        InAppNotifications = prefs.InAppNotifications,
                        Orders = prefs.Orders,
                        Deliveries = prefs.Deliveries,
                        Payments = prefs.Payments,
                        SystemAlerts = prefs.SystemAlerts,
                        Promotions = prefs.Promotions,
                        UpdatedAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    row.EmailNotifications = prefs.EmailNotifications;
                    row.SmsNotifications = prefs.SmsNotifications;
                    row.InAppNotifications = prefs.InAppNotifications;
                    row.Orders = prefs.Orders;
                    row.Deliveries = prefs.Deliveries;
                    row.Payments = prefs.Payments;
                    row.SystemAlerts = prefs.SystemAlerts;
                    row.Promotions = prefs.Promotions;
                    row.UpdatedAt = DateTime.UtcNow;
                }
                await _db.SaveChangesAsync(ct);
                Log.Information("🔔 Notification preferences saved for {Key}", key);
                return prefs;
            }
            catch (Exception ex) when (attempt < RaceRetries && IsTransientRace(ex))
            {
                // Two first-time saves for the same subject raced the UNIQUE
                // index, or SQLITE_BUSY locked the file behind another writer.
                // Discard the change set and re-run: the re-read sees the
                // winner's row and takes the UPDATE path — documented
                // contract is last-writer-wins, never a 500.
                Log.Debug("Preferences write raced on {Key} ({Error}); retrying", key, ex.GetType().Name);
                _db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(3 * (attempt + 1) + Random.Shared.Next(8)), ct);
            }
        }
    }

    // Issue #91: same provider-neutral race detection as the #33 registry —
    // see DeviceTokenRegistry.IsUniqueViolation. Under postgres the UNIQUE(Key)
    // loser surfaces as PostgresException 23505, not SqliteException 19, so the
    // old typed match was dead code on that provider and the last-writer-wins
    // contract this store documents was silently gone.
    private static bool IsTransientRace(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => true,
        _ when DeviceTokenRegistry.IsUniqueViolationForStore(ex) => true,
        _ when DeviceTokenRegistry.IsSqliteBusy(ex) => true,
        _ => false,
    };

    private static NotificationPreferencesDto ToDto(Models.NotificationPreferences row) => new()
    {
        EmailNotifications = row.EmailNotifications,
        SmsNotifications = row.SmsNotifications,
        InAppNotifications = row.InAppNotifications,
        Orders = row.Orders,
        Deliveries = row.Deliveries,
        Payments = row.Payments,
        SystemAlerts = row.SystemAlerts,
        Promotions = row.Promotions,
    };
}
