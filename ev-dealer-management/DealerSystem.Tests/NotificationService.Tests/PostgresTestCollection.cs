using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Every class that runs <c>Migrate()</c> against <c>EVM_TEST_POSTGRES</c>
/// belongs in this collection, and the collection runs exclusively.
///
/// WHY THE COLLECTION EXISTS (measured on PR #151, not reasoned). Two Postgres
/// test classes on a brand-new database reproduce this 3 out of 3 runs:
///
///   Npgsql.PostgresException : 23505: duplicate key value violates unique
///   constraint "pg_type_typname_nsp_index"
///   DETAIL: Key (typname, typnamespace)=(__EFMigrationsHistory, 2200) already exists.
///
/// <c>Migrate()</c> creates <c>__EFMigrationsHistory</c> by asking whether the
/// table exists and then creating it — a read and a later write with a gap
/// between them, which is the same shape as every other check-then-insert race
/// this repo has had to fix with an index. Postgres registers a table's
/// composite type in <c>pg_type</c> under a unique (typname, typnamespace)
/// index, so the second creator fails there rather than on the table itself.
/// The first creator wins; the loser dies at boot. That is what CI showed:
/// <c>PostgresDataSynchronizationTests.SynchronizeSales_...</c> failed on a
/// fresh runner with exactly the DETAIL above while its sibling class was
/// creating the same table.
///
/// It never fired before #151 because there was only ONE class calling
/// <c>Migrate()</c> — one creator cannot race itself. The suite's own doc
/// comment had recorded the assumption that made that safe: "Safe against a
/// database the running stack already migrated: Migrate() is a no-op once
/// __EFMigrationsHistory has the ids". True, and incomplete: on a fresh
/// database there is nothing yet to be a no-op against.
///
/// WHY DisableParallelization AND NOT JUST A SHARED COLLECTION. Members of one
/// collection are serialized against each other, but still run alongside every
/// other class. That would close today's race (these are the only two classes
/// that touch Postgres) and leave the door open for the third — a future class
/// that calls <c>Migrate()</c> and forgets the attribute would run in the
/// parallel burst right on top of them, and nothing would fail except in CI,
/// occasionally. With <c>DisableParallelization = true</c> this collection runs
/// in an exclusive phase with nothing else in flight, so an uncollected
/// sibling cannot overlap it either. Same reasoning and same shape as
/// <see cref="SqliteCollection"/>, which needs exclusivity for a
/// process-global pool reset; here the shared object is a table two classes
/// both try to create.
///
/// THE MEMBERSHIP RULE: every class whose <c>NewContext()</c>-equivalent calls
/// <c>db.Database.Migrate()</c> with <c>EVM_TEST_POSTGRES</c>. Nothing
/// enforces it — grep for <c>Database.Migrate</c> when adding a Postgres test
/// class, the same way the sqlite rule says to grep for ClearAllPools.
///
/// WHAT THIS DELIBERATELY DOES NOT FIX: the same race exists in production.
/// Six services boot against one shared database and each runs
/// <c>Migrate()</c>; on a first deploy to an empty database two of them can
/// collide the same way, and the loser exits instead of retrying. That is a
/// boot-time product problem (it needs a lock or a retry around migration),
/// not something a test collection can reach, and it is out of scope for the
/// change that surfaced it.
/// </summary>
[CollectionDefinition("postgres", DisableParallelization = true)]
public class PostgresCollection
{
    // No fixture: each class keeps its own connection string and its own
    // contexts; the collection exists purely to take the exclusive phase so
    // only one Migrate() runs against a fresh database at a time.
}
