using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #58: SqliteConnection.ClearAllPools() is a PROCESS-GLOBAL pool
/// reset, but xUnit runs test classes in parallel by default. Every
/// file-db test class in this assembly (temp SQLite + Dispose that clears
/// pools then deletes the file) therefore mutates shared state that the
/// other classes are mid-operation on. Observed once as a single unnamed
/// failure in PR #57's first suite run, and reproduced naturally during
/// #58: 1 of 20 real suite runs on unfixed main died in
/// PreferenceFanoutConsumerTests.Dispose with IOException "being used by
/// another process" — the classic rare cold-start race signature, the
/// kind of flake that eventually teaches everyone to re-run CI instead of
/// reading it.
///
/// The fix is the issue's option 1. How xUnit actually executes it (the
/// #58 review round verified this against a fresh TRX timeline, and the
/// first draft of this comment had it wrong): DisableParallelization=true
/// does NOT merely serialize the collection's members against each other
/// — it runs the collection in an EXCLUSIVE phase. All ordinary
/// parallelizable classes execute first as a burst, then nothing else is
/// in flight while the sqlite ladder runs alone. That is what closes the
/// race: no concurrent sibling exists at all during the window, rather
/// than "siblings that happen not to clear pools."
///
/// Consequences worth knowing:
/// - Cost is more than "the SQLite classes run serially". xUnit runs this
///   as two phases: first every ordinary parallelizable class in a burst
///   (EventContractTests, EventRetryPolicyTests,
///   FirebaseMulticastClassifierTests, DeviceTokensAuthTests — parallel
///   with EACH OTHER), THEN the sqlite ladder (~25s) runs ALONE with
///   nothing in flight. So the SQLite classes lose their cross-class
///   overlap entirely, and no class overlaps with them. (A fresh #58 TRX
///   timeline showed exactly this: parallel burst, then the ladder with
///   zero overlap on either side.)
/// - The membership rule — [Collection("sqlite")] on EVERY class that
///   calls ClearAllPools — is deliberately stricter than today's
///   exclusive phase requires. An uncollected clearer cannot race this
///   collection as configured; the rule exists so the day someone drops
///   DisableParallelization (e.g. to reclaim suite time), the classes
///   that share the process-global pool are still exactly the classes
///   that are serialized, instead of silently re-opening the race.
/// - Nothing mechanically enforces the rule (no analyzer, no CI check).
///   Grep for ClearAllPools when adding a file-db class.
/// - QuotePdfGenerationTests stays uncollected: `Data Source=:memory:`,
///   no Dispose/ClearAllPools, and SalesController.GenerateQuotePdf never
///   queries the context — there is no pool and no handle to race. If it
///   ever gains real db work, add [Collection("sqlite")] to it.
///
/// Acceptance is the issue's own 50 consecutive green full-suite runs
/// (recorded on the PR thread; met 50/50 on Windows plus CI's Ubuntu
/// runs). No achievable run-count proves the serialization sufficient —
/// the steady-state canary (SqliteCrossTalkStressTests) reproduced
/// nothing (960 cycles, 0 failures) — so the fix rests on mechanism: a
/// process-global reset cannot be scoped per-class without abandoning
/// ClearAllPools wholesale (options 2/3, far more surgery), and an
/// exclusive phase is the only thing a collection can offer against it.
/// </summary>
[CollectionDefinition("sqlite", DisableParallelization = true)]
public class SqliteCollection
{
    // No fixture: each class keeps its own temp-file db + disposal; the
    // collection exists purely to force the classes onto one serial worker.
}
