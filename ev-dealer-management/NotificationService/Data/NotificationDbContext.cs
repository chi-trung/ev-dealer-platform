using Microsoft.EntityFrameworkCore;
using NotificationService.Models;

namespace NotificationService.Data;

/// <summary>
/// Tiny owned-by-this-service database for the DeviceToken registry
/// (Issue #33) and per-subject notification preferences (Issue #51).
/// Deliberately two small tables with no FKs: both only ever matter to the
/// push senders and the settings page living in/behind this process.
/// </summary>
public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options)
    {
    }

    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DeviceToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Lookup path is always key-equality at delivery time; the
            // (Key, Token) index also enforces "same browser registered
            // twice updates, never duplicates".
            entity.HasIndex(e => new { e.Key, e.Token }).IsUnique();
            entity.Property(e => e.Key).IsRequired();
            entity.Property(e => e.Token).IsRequired();
            // Issue #44 review: the eviction victim is picked by a read that
            // commits BEFORE SaveChangesAsync opens its transaction, so a
            // DELETE keyed only on Id could silently delete a row a
            // concurrent refresh just made the MOST recently used device.
            // Keying the write on the read-time UpdatedAt turns that
            // write-skew into a 0-row match → DbUpdateConcurrencyException →
            // the retry loop's re-read picks a genuinely stale victim.
            entity.Property(e => e.UpdatedAt).IsConcurrencyToken();
        });

        modelBuilder.Entity<NotificationPreferences>(entity =>
        {
            entity.HasKey(e => e.Id);
            // One preferences document per subject — PUT upserts on this key
            // (the UNIQUE hit under a concurrent first-save is the race the
            // store's retry loop absorbs, mirroring the #33 registry).
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Key).IsRequired();
        });
    }
}
