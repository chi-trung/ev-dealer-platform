using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #58: SqliteConnection.ClearAllPools() is a PROCESS-GLOBAL pool
/// reset, but xUnit runs test classes in parallel by default. Every
/// file-db test class in this assembly (temp SQLite + Dispose that clears
/// pools then deletes the file) therefore mutates shared state that the ten
/// other classes are mid-operation on. Observed once as a single unnamed
/// failure in PR #57's first suite run; ~30 consecutive clean runs after —
/// the classic cold-start race signature: rare, load-dependent, and the
/// kind of flake that eventually teaches everyone to re-run CI instead of
/// reading it.
///
/// The fix is the issue's option 1: one collection, no parallelism INSIDE
/// it. [CollectionDefinition(DisableParallelization = true)] serializes all
/// classes that carry [Collection("sqlite")] — while the non-SQLite classes
/// (EventContractTests, EventRetryPolicyTests, FirebaseMulticastClassifier
/// Tests, DeviceTokensAuthTests) still run in parallel with the collection
/// and with each other. QuotePdfGenerationTests is the deliberate
/// near-miss: `Data Source=:memory:` with NO Dispose/ClearAllPools and a
/// controller that never queries the db, so a pool drop mid-test is
/// unobservable for it; if it ever gains real db work, add
/// [Collection("sqlite")] to it.
///
/// The one rule that makes this work: NOTHING that calls ClearAllPools may
/// sit outside the collection — a collection only serializes its members,
/// so a single uncollected clearer re-opens the race for all of them. That
/// is also why the lifecycle canaries (SqliteCrossTalkStressTests) are
/// collected: their value is being a permanent tripwire, not load.
///
/// Cost: the SQLite classes (~25s of the suite) run serially. Acceptance
/// is the issue's own 50 consecutive green full-suite runs, recorded on
/// the PR thread. What #58 actually measured, in order: (1) the race
/// reproduced NATURALLY on unfixed main — 1 of 20 real suite runs died in
/// PreferenceFanoutConsumerTests.Dispose with IOException "being used by
/// another process", the PR #57 signature; (2) 8 UNCOLLECTED canary copies
/// x 120 cycles reproduced nothing — the collision needs the real suite's
/// class schedule, not a tight loop, so no achievable run-count PROVES the
/// serialization sufficient; it rests on mechanism instead — a
/// process-global pool reset cannot be scoped per-class without abandoning
/// ClearAllPools wholesale (the issue's options 2/3).
/// </summary>
[CollectionDefinition("sqlite", DisableParallelization = true)]
public class SqliteCollection
{
    // No fixture: each class keeps its own temp-file db + disposal; the
    // collection exists purely to force the classes onto one serial worker.
}
