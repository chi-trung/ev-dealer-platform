using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

// Issue #58 lifecycle canary — what it IS, and two things it is NOT.
//
// IS: a repeated end-to-end check of the pattern every real SQLite class
// depends on — temp file db + EnsureCreated + pooled write/read + the
// process-global ClearAllPools + the File.Delete that only succeeds once
// the clear really released the handle. 240 lifetimes per suite run catch
// a regression in that release-and-delete mechanism itself.
//
// NOT a race reproducer: run UNCOLLECTED before the fix, 8 copies x 120
// cycles = 960 iterations, 0 failures. The race needs the real suite's
// class schedule; on unfixed main the REAL suite reproduced it naturally
// (1 of 20 runs: PreferenceFanoutConsumerTests.Dispose, IOException
// "being used by another process" — the PR #57 signature), and that is
// the before-fix evidence. No synthetic loop earns it.
//
// NOT a collection-membership tripwire either, which an earlier comment
// here wrongly claimed (the #58 review round, via TRX timeline): each
// cycle uses a unique-Guid file and deletes it right after its OWN clear,
// so another class's clear can only help, never hurt — and
// DisableParallelization puts this collection in an exclusive phase, so
// these cycles never co-run with an uncollected offender anyway. Two
// uncollected clearest WOULD race each other in the parallel burst phase
// (that is how main flaked: all clearest were uncollected together); one
// uncollected clearer is harmless only as long as the collection keeps
// DisableParallelization — another reason the membership rule is kept
// strict. Membership of every ClearAllPools caller is enforced only by
// grep (see SqliteTestCollection.cs).

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
