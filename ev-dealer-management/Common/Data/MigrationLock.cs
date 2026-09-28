using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Common.Data;

/// <summary>
/// Applies one service's migrations without racing the five others that boot
/// against the same database.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. All six database-backed services share one Postgres
/// database and therefore one <c>__EFMigrationsHistory</c> table, and docker
/// compose brings them up concurrently (the gateway depends on them with
/// <c>service_started</c>, not <c>service_healthy</c>) while Render has no
/// <c>preDeployCommand</c> at all. <c>Migrate()</c> creates the history table by
/// asking whether it exists and then creating it — a read and a later write
/// with a gap between them — so two services starting against an EMPTY
/// database collide. Measured, on a fresh database, three times out of three:
///
///   Npgsql.PostgresException : 23505: duplicate key value violates unique
///   constraint "pg_type_typname_nsp_index"
///   DETAIL: Key (typname, typnamespace)=(__EFMigrationsHistory, 2200) already exists.
///
/// Postgres records every table's composite type in <c>pg_type</c> under a
/// unique (typname, typnamespace) index, so the loser fails there rather than
/// on the table itself. This already broke CI once (PR #151, where the two
/// Postgres test classes raced); the test-side fix that made CI green — an
/// exclusive xUnit collection — cannot reach processes, so this is the
/// production half. Three of the six services would die of it (UserService,
/// VehicleService, CustomerService rethrow to their top-level
/// <c>Log.Fatal</c>); the other three would swallow it and boot green with no
/// schema, which is the failure mode UserService's own comment calls worse
/// than a crashloop.
///
/// WHY AN ADVISORY LOCK AND NOT A RETRY. A retry on the 23505 would have to be
/// narrow enough to tell "the history table lost the race" apart from a
/// legitimate unique violation — <c>MakeUserEmailUnique</c> can fail with
/// exactly 23505 when existing rows share an address, and swallowing that
/// boots green with the migration never applied. The lock removes the window
/// instead of recovering from it, needs no error message parsing, and covers
/// every participant that takes it. Pre-creating the table with
/// <c>CREATE TABLE IF NOT EXISTS</c> was rejected too: Postgres documents that
/// as NOT race-free, so it narrows the window without closing it.
///
/// WHY ONE KEY FOR EVERY SERVICE. A per-service key would let all six run at
/// once, which is the bug. The key is derived from the ASCII of "EVM_MIG" so
/// it reads as intentional in a <c>pg_locks</c> dump.
///
/// WHY THE LOCK IS RELEASED EXPLICITLY. It is session-scoped: it survives
/// whatever the connection does next, so a connection that outlives this call
/// (EF keeps the scoped connection open for the rest of the process) would
/// hold the lock until the process exits — and the next service to boot would
/// block on it forever rather than fail. Worse, the failure would look like a
/// hang, not an error. <c>finally</c> releases it whether <c>Migrate()</c>
/// succeeded or threw, and a closed session releases it anyway as a backstop.
///
/// WHY SQLITE FALLS THROUGH. Under <c>DB_PROVIDER=sqlite</c> — the default for
/// local runs, and what the test suite uses — each service has its own
/// <c>.db</c> file and therefore its own history table, so there is nothing to
/// contend over. Advisory locks do not exist there either. A plain
/// <c>Migrate()</c> is exactly right, and no provider check is needed beyond
/// the connection type: <c>GetDbConnection()</c> is already concrete.
///
/// Failure policy is deliberately NOT handled here. Whether a migration error
/// kills the process or is swallowed is each service's existing, deliberate
/// choice (see the try/catch around every call site) and is not this change's
/// to make — so a failure of the lock statement itself propagates to that same
/// catch, exactly as a <c>Migrate()</c> failure does today.
/// </remarks>
public static class MigrationLock
{
    /// <summary>
    /// ASCII "EVM_MIG" (45 56 4D 5F 4D 49 47). One constant, shared by every
    /// service on purpose — see the class remarks.
    ///
    /// Public rather than private so the release test can probe for THIS key
    /// from a second connection. A copy of the value in the test would go on
    /// passing after the constant changed, which is the shape of test this
    /// repo keeps getting rid of.
    /// </summary>
    public const long AdvisoryLockKey = 0x45564D5F4D4947L;

    /// <summary>
    /// Runs <see cref="DatabaseFacade.Migrate()"/> while holding a Postgres
    /// advisory lock, so only one service at a time can be creating
    /// <c>__EFMigrationsHistory</c> or appending to it.
    /// </summary>
    /// <param name="db">A context whose provider decides whether the lock is taken.</param>
    public static void Migrate(DbContext db)
    {
        var connection = db.Database.GetDbConnection();

        // Not Postgres: nothing shared to serialise against (see remarks).
        if (connection is not NpgsqlConnection)
        {
            db.Database.Migrate();
            return;
        }

        // Open the connection OURSELVES so the lock lands on the same session
        // Migrate() will use. If we left it closed and let EF open it, the lock
        // could sit on one backend while the migration ran on another and would
        // protect nothing — the failure mode that would make this file look
        // correct and do nothing, which is why the concurrency test asserts on
        // outcomes rather than on the SQL being present.
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) connection.Open();

        using (var acquire = connection.CreateCommand())
        {
            acquire.CommandText = "SELECT pg_advisory_lock(@key);";
            acquire.Parameters.Add(NewKeyParameter(acquire));
            acquire.ExecuteNonQuery();
        }

        try
        {
            db.Database.Migrate();
        }
        finally
        {
            // Always release — see the remarks on why a held lock reads as a
            // hang in the NEXT service rather than as an error in this one.
            using var release = connection.CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(@key);";
            release.Parameters.Add(NewKeyParameter(release));
            release.ExecuteNonQuery();

            if (openedHere) connection.Close();
        }
    }

    private static System.Data.Common.DbParameter NewKeyParameter(
        System.Data.Common.DbCommand command)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = AdvisoryLockKey;
        return parameter;
    }
}
