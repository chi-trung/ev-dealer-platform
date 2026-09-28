using Common.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// The production half of the <c>__EFMigrationsHistory</c> creation race —
/// the one a test collection cannot fix, because six processes do not share an
/// xUnit scheduler.
///
/// WHAT IT REPRODUCES. Six services, one Postgres, one history table, empty
/// database. <c>Migrate()</c> asks whether <c>__EFMigrationsHistory</c> exists
/// and then creates it, so two of them race the same read-then-write and the
/// loser gets <c>23505 pg_type_typname_nsp_index</c> (Postgres records a
/// table's composite type under a unique typname/typnamespace index). Measured
/// three times out of three with just two contexts before the fix, which is
/// why six callers here are not decoration: one extra service multiplies the
/// window rather than the confidence.
///
/// WHY EACH SERVICE IS ITS OWN CONTEXT TYPE. The six contexts have disjoint
/// tables, exactly as production does — the race is on the shared history
/// table, not on anyone's schema, and using six copies of one context would
/// add a second, unrelated collision (two processes applying the same
/// migration's DDL) that no service ever experiences. Note that
/// <c>global::NotificationService</c> needs the qualifier: this namespace is
/// <c>DealerSystem.Tests.NotificationService.Tests</c>, so the bare identifier
/// binds to the enclosing test namespace, which has no <c>Data</c>.
///
/// WHY THE CALLS ARE ON DEDICATED THREADS (TaskCreationOptions.LongRunning)
/// RATHER THAN PLAIN Task.Run. Six ordinary tasks may sit behind thread-pool
/// injection while the first one finishes, and then the run is sequential by
/// accident. A sequential run passes whether or not the lock exists — that is
/// the exact defect that made the first version of the email-race test
/// worthless, and it was only caught by breaking the code on purpose. One
/// dedicated thread each makes "all six are waiting on the gate" a property of
/// the test, not of the scheduler.
///
/// WHY THE DATABASE IS A SCRATCH ONE. The suite is often pointed at the same
/// database the running stack uses (<c>evm_core</c>). Dropping or migrating
/// that would be destructive, so each test creates and drops its own database;
/// the recipe is the same one that reproduced the original CI failure on a
/// fresh runner.
/// </summary>
[Collection("postgres")]
public class PostgresConcurrentMigrateTests
{
    /// <summary>
    /// Unique per test, because xUnit constructs a new instance of this class
    /// for each test. A shared name was tried first and failed 3 runs out of
    /// 3 — not on the race, but on the pool: two tests pointing at one
    /// database name share one Npgsql connection pool, so the first test's
    /// <c>DROP DATABASE ... WITH (FORCE)</c> kills backends the second test
    /// then borrows. It surfaced as "connection aborted by the software in
    /// your host machine" on the unlock, which reads like a fault in the lock
    /// and is nothing of the sort. A distinct database name gives each test
    /// its own pool, so cleanup cannot reach into the next test.
    /// </summary>
    private readonly string _scratchDatabase = $"evm_migrate_race_{Guid.NewGuid():N}";

    private readonly ITestOutputHelper _out;

    public PostgresConcurrentMigrateTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// One entry per database-backed service, in boot order. Types are spelled
    /// fully qualified because the test namespace shadows
    /// <c>NotificationService</c> (see the class remarks).
    /// </summary>
    private static readonly (string Service, Func<string, DbContext> Create)[] Services =
    {
        ("UserService", cs => new UserService.Data.UserDbContext(Options<UserService.Data.UserDbContext>(cs))),
        ("VehicleService", cs => new VehicleService.Data.ApplicationDbContext(Options<VehicleService.Data.ApplicationDbContext>(cs))),
        ("CustomerService", cs => new CustomerService.Data.CustomerDbContext(Options<CustomerService.Data.CustomerDbContext>(cs))),
        ("NotificationService", cs => new global::NotificationService.Data.NotificationDbContext(Options<global::NotificationService.Data.NotificationDbContext>(cs))),
        ("ReportingService", cs => new ev_dealer_reporting.Data.ReportingDbContext(Options<ev_dealer_reporting.Data.ReportingDbContext>(cs))),
        ("SalesService", cs => new SalesService.Data.SalesDbContext(Options<SalesService.Data.SalesDbContext>(cs))),
    };

    private static DbContextOptions<T> Options<T>(string connectionString)
        where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(connectionString).Options;

    private static string BaseConnectionString =>
        Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)
            ?? throw new InvalidOperationException("unreachable: PostgresFact skips before the body runs");

    private string ScratchConnectionString =>
        new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = _scratchDatabase }.ConnectionString;

    private async Task CreateScratchDatabaseAsync()
    {
        // WITH (FORCE) so a previous run that died before its finally block
        // cannot leave a connection holding the database open and turn the
        // cleanup failure into a second, misleading error.
        await ExecuteAdminAsync(
            $"DROP DATABASE IF EXISTS \"{_scratchDatabase}\" WITH (FORCE); " +
            $"CREATE DATABASE \"{_scratchDatabase}\";");
    }

    private Task DropScratchDatabaseAsync() =>
        ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{_scratchDatabase}\" WITH (FORCE);");

    private static async Task ExecuteAdminAsync(string sql)
    {
        var admin = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ReadHistoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";""";
        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        return ids.ToArray();
    }

    /// <summary>
    /// Six services, empty database, released together. Every one of them must
    /// come back clean and every service's own migrations must be recorded —
    /// "no exception" alone would not notice a service that silently migrated
    /// nothing, and the fail-soft call sites in production make that the
    /// failure mode worth ruling out.
    /// </summary>
    [PostgresFact]
    public async Task SixServicesMigratingAnEmptyDatabaseAllSucceed()
    {
        await CreateScratchDatabaseAsync();
        try
        {
            var gate = new TaskCompletionSource();
            var tasks = Services.Select(service => Task.Factory.StartNew(
                () =>
                {
                    using var db = service.Create(ScratchConnectionString);
                    // All six wait here, so they are released together and all
                    // of them reach Migrate() before the first one finishes.
                    gate.Task.Wait();
                    MigrationLock.Migrate(db);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)).ToArray();

            gate.SetResult();

            // Any one of them losing the race throws here, with the 23505 as
            // the message — which is the red this test exists to produce when
            // the lock is taken away.
            await Task.WhenAll(tasks);

            var history = await ReadHistoryAsync(ScratchConnectionString);
            _out.WriteLine($"history rows={history.Length}");

            foreach (var service in Services)
            {
                using var db = service.Create(ScratchConnectionString);
                var defined = db.Database.GetMigrations().ToArray();
                Assert.NotEmpty(defined);

                foreach (var migration in defined)
                {
                    Assert.True(
                        history.Contains(migration),
                        $"{service.Service} did not apply {migration}: " +
                        $"history holds [{string.Join(", ", history)}]");
                }
            }

            // Exactly once each: a service that re-ran a migration, or one
            // that never ran, both show up in this count.
            var totalDefined = Services.Sum(service =>
            {
                using var db = service.Create(ScratchConnectionString);
                return db.Database.GetMigrations().Count();
            });
            Assert.Equal(totalDefined, history.Length);
        }
        finally
        {
            await DropScratchDatabaseAsync();
        }
    }

    /// <summary>
    /// The lock must be gone before <c>Migrate()</c> returns, otherwise the
    /// NEXT service to boot blocks on it forever. That failure would present
    /// as a hang with no log line rather than as an error, so it is worth a
    /// test of its own.
    ///
    /// The connection is opened first on purpose: <c>MigrationLock</c> only
    /// releases the lock explicitly when it did not open the connection
    /// itself, and closing an opened session would drop the lock either way —
    /// which would make this test pass with the <c>pg_advisory_unlock</c> line
    /// deleted. Holding the connection open is what pins the unlock.
    /// </summary>
    [PostgresFact]
    public async Task TheAdvisoryLockIsReleasedEvenWhenTheConnectionWasAlreadyOpen()
    {
        await CreateScratchDatabaseAsync();
        try
        {
            using var db = new UserService.Data.UserDbContext(
                Options<UserService.Data.UserDbContext>(ScratchConnectionString));
            db.Database.OpenConnection();
            MigrationLock.Migrate(db);

            // A different session entirely — the same session can re-acquire
            // its own advisory lock, so probing from here would say "free"
            // even while it is held.
            await using var probe = new NpgsqlConnection(ScratchConnectionString);
            await probe.OpenAsync();
            await using var command = probe.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key);";
            command.Parameters.AddWithValue("key", MigrationLock.AdvisoryLockKey);
            var acquired = (bool)(await command.ExecuteScalarAsync() ?? false);

            Assert.True(
                acquired,
                "the advisory lock was still held after Migrate() returned — " +
                "the next service to boot would wait on it forever instead of failing loudly");

            // Hand it back so the database can be dropped without FORCE
            // needing to wait on anything.
            await using var release = probe.CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(@key);";
            release.Parameters.AddWithValue("key", MigrationLock.AdvisoryLockKey);
            await release.ExecuteScalarAsync();
        }
        finally
        {
            await DropScratchDatabaseAsync();
        }
    }
}
