using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #92 (P2): NotificationService was the only service still calling
/// <c>EnsureCreated()</c> instead of <c>Migrate()</c>, and that difference
/// is a live production bug rather than a consistency nit — render.yaml wires
/// ALL SIX database-backed services to ONE Postgres database
/// (<c>evm-postgres</c> / <c>databaseName: evm_core</c>, referenced at
/// lines 188, 265, 303, 345, 387 and 427). The services hold disjoint table
/// sets, so sharing one database is the design (VehicleService owns Dealers;
/// UserService deliberately does not map it — Issue #121).
///
/// <c>EnsureCreated()</c> gates on whether the DATABASE has any tables, not
/// on whether ITS OWN tables exist. On Render the database is never empty
/// when NotificationService boots — whichever of the other five services
/// started first has already created its own tables — so
/// <c>EnsureCreated()</c> returns false having created NOTHING, and the
/// DeviceToken registry and NotificationPreferences tables simply do not
/// exist. The explicit <c>CREATE TABLE IF NOT EXISTS</c> that follows only
/// covers NotificationPreferences, so DeviceTokens — the table the 14 queue
/// consumers read on every push lookup — is the one that goes missing. Events
/// then degrade to log-only, which is exactly the pre-Issue-#33 behaviour
/// that #33 was opened to fix.
///
/// These tests pin that failure mode on a real SQLite FILE (not the in-memory
/// provider: only a real file exercises the "database is not empty" gate).
/// No Postgres needed — the gate is provider-agnostic, so the same bug
/// reproduces locally and in CI.
/// </summary>
[Collection("sqlite")]
public class EnsureCreatedSharedDatabaseTests : IDisposable
{
    private readonly string _dbPath;

    public EnsureCreatedSharedDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"shared_db_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private DbContextOptions<NotificationDbContext> Options() =>
        new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

    /// <summary>
    /// Stand in for the other five services on the shared Render database:
    /// create ONE unrelated table, which is all it takes to make the
    /// database non-empty.
    /// </summary>
    private void SimulateAnotherServiceOnSharedDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        // Table + column names taken from VehicleService's baseline migration
        // so this really is the shape a sibling service leaves behind.
        command.CommandText =
            "CREATE TABLE \"Dealers\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Dealers\" PRIMARY KEY AUTOINCREMENT);";
        command.ExecuteNonQuery();
    }

    private bool TableExists(string name)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    [Fact]
    public void EnsureCreated_AfterAnotherServiceCreatedATable_CreatesNothing()
    {
        // THE bug. Assert the PREMISE too: without this, a future EF change
        // that made EnsureCreated smarter would leave the test passing for
        // the wrong reason and the real-world exposure undocumented.
        SimulateAnotherServiceOnSharedDatabase();
        Assert.True(TableExists("Dealers"), "premise: sibling table must exist");

        using var db = new NotificationDbContext(Options());
        var created = db.Database.EnsureCreated();

        Assert.False(created);
        Assert.False(TableExists("DeviceTokens"),
            "EnsureCreated() returned false and left DeviceTokens absent — the "
            + "14 queue consumers read this table on every push lookup, so every "
            + "delivery fails on Render today.");

        // The follow-up CREATE TABLE IF NOT EXISTS in Program.cs covers
        // NotificationPreferences only, which is why the registry table is the
        // one that actually disappears.
        Assert.True(db.Database.ExecuteSqlRaw(
            """CREATE TABLE IF NOT EXISTS "NotificationPreferences" ("Id" INTEGER NOT NULL CONSTRAINT "PK_NotificationPreferences" PRIMARY KEY AUTOINCREMENT, "Key" TEXT NOT NULL, "EmailNotifications" INTEGER NOT NULL DEFAULT 0, "SmsNotifications" INTEGER NOT NULL DEFAULT 0, "InAppNotifications" INTEGER NOT NULL DEFAULT 0, "Orders" INTEGER NOT NULL DEFAULT 0, "Deliveries" INTEGER NOT NULL DEFAULT 0, "Payments" INTEGER NOT NULL DEFAULT 0, "SystemAlerts" INTEGER NOT NULL DEFAULT 0, "Promotions" INTEGER NOT NULL DEFAULT 0, "UpdatedAt" TEXT NOT NULL);""") >= 0);
        Assert.True(TableExists("NotificationPreferences"),
            "the manual DDL still creates the preferences table — DeviceTokens is "
            + "the gap, and it is the one the consumers need");
    }

    [Fact]
    public void EnsureCreated_OnAnEmptyDatabase_CreatesTheRegistrySchema()
    {
        // The other observable branch, and the reason the bug hid for so long:
        // on a FRESH database (local dev, CI, a brand-new Render instance
        // where NotificationService happened to win the race) EnsureCreated
        // does create both tables and everything works. Only the shared-database
        // case is broken.
        using var db = new NotificationDbContext(Options());

        Assert.True(db.Database.EnsureCreated());
        Assert.True(TableExists("DeviceTokens"));
        Assert.True(TableExists("NotificationPreferences"));
    }

    [Fact]
    public void Migrate_CreatesTheRegistrySchema_EvenOnANonEmptySharedDatabase()
    {
        // The fix's contract. A hand-authored baseline migration (mirroring what
        // UserService, VehicleService, CustomerService, SalesService and
        // ReportingService already ship) makes the registry schema creation
        // independent of whether a sibling service got there first — because
        // Migrate() is driven by __EFMigrationsHistory, not by "is the database
        // empty".
        SimulateAnotherServiceOnSharedDatabase();
        using var db = new NotificationDbContext(Options());
        db.Database.Migrate();

        Assert.True(TableExists("DeviceTokens"));
        Assert.True(TableExists("NotificationPreferences"));

        // And the baseline must be recorded exactly once, so a second service
        // boot is a no-op instead of a re-create attempt.
        var applied = new List<string>();
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """SELECT "MigrationId" FROM "__EFMigrationsHistory";""";
            using var reader = command.ExecuteReader();
            while (reader.Read()) applied.Add(reader.GetString(0));
        }
        Assert.Equal(new[] { "20260925083608_NotificationBaseline" }, applied);
    }
}
