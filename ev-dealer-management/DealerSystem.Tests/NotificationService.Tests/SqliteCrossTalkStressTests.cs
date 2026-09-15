using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

// Issue #58 lifecycle canary. Eleven real classes in this assembly Dispose
// with SqliteConnection.ClearAllPools() — a PROCESS-GLOBAL pool reset — and
// that is the race the [Collection("sqlite")] fix serializes (see
// SqliteTestCollection.cs for the full picture).
//
// Measured history, in order:
// 1. 8 UNCOLLECTED copies of this cycle x 120 iterations: 0 failures. The
//    PR #57 race is cold-start/one-in-many-runs, so a steady-state hammer
//    is NOT a reproducer and is not sold as one.
// 2. Running the REAL suite on unfixed main (d9abc78) did reproduce it
//    naturally: 1 of 20 runs died in PreferenceFanoutConsumerTests.Dispose
//    with IOException "being used by another process" — the exact PR #57
//    signature. That is the before-fix evidence; it needed no canary.
// 3. The uncollected version then became a liability: a collection only
//    serializes its MEMBERS, so an uncollected class calling ClearAllPools
//    can still drop the pooled connections of a collected class mid-run —
//    i.e. these very tests would have re-opened the race #58 fixes.
//
// So they are [Collection("sqlite")] like every other clearer. Collected,
// each cycle is a pure lifecycle assertion: file db + EnsureCreated +
// pooled write/read + the global clear + the delete that only succeeds if
// the clear really released the handle. If anyone un-collects a clearer in
// the future, this file is the one that starts failing.

[Collection("sqlite")]
public abstract class CrossTalkHammerBase
{
    /// <summary>One full lifetime of the real-world pattern: file db +
    /// pooled writes/reads + the global clear + the delete that depends on
    /// it. Throws (failing the test) if the clear didn't release the
    /// handle.</summary>
    protected static async Task CycleAsync(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        using (var db = new NotificationDbContext(options))
        {
            db.Database.EnsureCreated();
            for (var i = 0; i < 20; i++)
                db.DeviceTokens.Add(new DeviceToken { Key = "user:stress", Token = $"{prefix}-tok-{i}" });
            await db.SaveChangesAsync();
        }

        using (var db = new NotificationDbContext(options))
            Assert.Equal(20, await db.DeviceTokens.CountAsync(t => t.Key == "user:stress"));

        SqliteConnection.ClearAllPools();
        File.Delete(path); // throws IOException on Windows while any handle lingers
        Assert.False(File.Exists(path));
    }
}

public class CrossTalkHammer1 : CrossTalkHammerBase
{
    [Fact] public async Task Hammers() { for (var i = 0; i < 120; i++) await CycleAsync("stress1"); }
}
public class CrossTalkHammer2 : CrossTalkHammerBase
{
    [Fact] public async Task Hammers() { for (var i = 0; i < 120; i++) await CycleAsync("stress2"); }
}
