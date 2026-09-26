using CustomerService.Data;
using CustomerService.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Pins the Customer-to-User bridge column added on this branch
/// (Customers.UserId + its unique index).
///
/// WHY A REAL SQLITE FILE AND NOT THE IN-MEMORY PROVIDER. The whole point of
/// the column is a database-level constraint — two customers may not point at
/// one account, or their notifications would merge. EF's InMemory provider
/// ignores unique indexes entirely, so a test written against it would pass
/// while the database let the duplicate through. The existing suite already
/// works this way (see the csproj comment about the (Key, Token) index), and
/// this class follows suit rather than inventing a third approach.
///
/// WHY THIS MATTERS WHILE THE COLUMN HAS NO WRITER. Nothing in the codebase
/// populates Customers.UserId yet, so the index is currently the ONLY thing
/// standing between a future writer and a silent merge of two customers'
/// notification subjects. A constraint with no test is one refactor away from
/// being dropped, and it would drop silently: removing IsUnique() compiles
/// cleanly, changes no existing test, and only misroutes push notifications.
///
/// NOTE ON FOREIGN KEYS. There is deliberately no FK to Users here — Users is
/// created by UserService's own migration, and each service Migrate()s only
/// its own set, so a real FK would make CustomerService fail to boot on a
/// fresh database whenever it starts before UserService. These tests
/// therefore do NOT assert referential integrity; they assert the two
/// properties the column actually has.
/// </summary>
public class CustomerUserLinkTests
{
    /// <summary>
    /// A real SQLite file in a temp directory, disposed with the test. The
    /// connection string is built by hand rather than through the shared test
    /// helpers so this class states the one thing it depends on — a real
    /// relational provider — at the point of use.
    /// </summary>
    private static CustomerDbContext NewDb(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"customerlink-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var db = new CustomerDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static Customer NewCustomer(string email, int? userId) => new()
    {
        Name = "Test Customer",
        Email = email,
        DealerId = 1,
        Status = "Active",
        JoinDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UserId = userId,
    };

    /// <summary>
    /// The load-bearing assertion. Two customers pointing at one account is
    /// the failure this index exists to stop, and it is invisible above the
    /// database: both rows save cleanly and the merge only shows up later as
    /// one person receiving another customer's order notifications.
    /// </summary>
    [Fact]
    public void TwoCustomersCannotPointAtTheSameUser()
    {
        using var db = NewDb(out var path);
        try
        {
            db.Customers.Add(NewCustomer("first@x.local", userId: 7));
            db.SaveChanges();

            db.Customers.Add(NewCustomer("second@x.local", userId: 7));

            // Assert.Throws, not Assert.ThrowsAsync: SaveChanges opens and
            // commits its own transaction, and a unique-index violation
            // surfaces synchronously from it.
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        finally
        {
            SqliteCleanup(path);
        }
    }

    /// <summary>
    /// The other direction of the same constraint: many customers may be
    /// unlinked, because nothing writes the column yet. If this ever fails,
    /// the column was made NOT NULL and every unlinked customer is now
    /// unwritable — which would have broken POST /api/Customers outright.
    /// </summary>
    [Fact]
    public void AnyNumberOfCustomersMayBeUnlinked()
    {
        using var db = NewDb(out var path);
        try
        {
            db.Customers.Add(NewCustomer("a@x.local", userId: null));
            db.Customers.Add(NewCustomer("b@x.local", userId: null));
            db.Customers.Add(NewCustomer("c@x.local", userId: null));

            db.SaveChanges();

            Assert.Equal(3, db.Customers.Count(c => c.UserId == null));
        }
        finally
        {
            SqliteCleanup(path);
        }
    }

    /// <summary>
    /// Distinct users, distinct customers — the shape the column exists to
    /// express. Without this, a mutation that made the index reject everything
    /// (or made the column unusable) could still satisfy the two tests above.
    /// </summary>
    [Fact]
    public void DistinctUsersLinkToDistinctCustomers()
    {
        using var db = NewDb(out var path);
        try
        {
            db.Customers.Add(NewCustomer("u1@x.local", userId: 1));
            db.Customers.Add(NewCustomer("u2@x.local", userId: 2));
            db.Customers.Add(NewCustomer("unlinked@x.local", userId: null));

            db.SaveChanges();

            var linked = db.Customers.Where(c => c.UserId != null).ToList();
            Assert.Equal(2, linked.Count);
            Assert.Equal(2, linked.Select(c => c.UserId).Distinct().Count());
        }
        finally
        {
            SqliteCleanup(path);
        }
    }

    /// <summary>
    /// Guards the scan-and-build this class rests on: if the model ever stops
    /// mapping the column, the two tests above would keep passing while
    /// testing nothing — every value would silently read back as null. This
    /// asks the model, not the database, so it fails at the point the mapping
    /// is dropped rather than at the point a notification goes missing.
    /// </summary>
    [Fact]
    public void TheModelMapsTheColumnAtAll()
    {
        using var db = NewDb(out var path);
        try
        {
            var property = db.Model.FindEntityType(typeof(Customer))?
                .FindProperty(nameof(Customer.UserId));

            Assert.NotNull(property);
            // Nullable int, not int: an unlinked customer must be
            // representable, which is the state every customer is in today.
            Assert.Equal(typeof(int?), property!.ClrType);
        }
        finally
        {
            SqliteCleanup(path);
        }
    }

    private static void SqliteCleanup(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }
}
