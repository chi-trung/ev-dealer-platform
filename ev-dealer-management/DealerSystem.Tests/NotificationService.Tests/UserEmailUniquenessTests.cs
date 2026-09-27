using Microsoft.EntityFrameworkCore;
using UserService.Data;
using UserService.Models;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Pins the unique index on Users.Email (issue #150).
///
/// WHY THIS TEST EXISTS. Every path that creates a User checks for a duplicate
/// email with AnyAsync first, which reads as though the database is covered.
/// It is not: the check and the insert are separate statements, so two
/// concurrent requests both see "no match" and both insert. Measured against
/// Postgres on the pre-change schema, two rows with the same email both
/// succeeded — the index was a plain CREATE INDEX, not a unique one. Username
/// was already unique and would have caught the race, but CustomerService
/// derives Username FROM Email, so the email is what a customer is created
/// under and it has to carry the guarantee itself.
///
/// WHY A REAL SQLITE FILE AND NOT THE IN-MEMORY PROVIDER. The subject here is
/// a database-level constraint, and EF's InMemory provider ignores unique
/// indexes entirely — a test written against it would pass while the database
/// let the duplicate through. CustomerUserLinkTests works the same way for the
/// same reason.
///
/// WHY THE MUTATION NOTE MATTERS. Removing .IsUnique() from
/// UserDbContext.OnModelCreating compiles cleanly, changes no other test in
/// this assembly, and leaves production quietly accepting two accounts with
/// one email. Nothing else in the suite would notice.
/// </summary>
[Collection("sqlite")]
public class UserEmailUniquenessTests : IDisposable
{
    private readonly string _path;
    private readonly UserDbContext _db;

    public UserEmailUniquenessTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"useremail-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;
        _db = new UserDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static User NewUser(string username, string email) => new()
    {
        Username = username,
        Email = email,
        FullName = "Test User",
        PasswordHash = "x",
        Role = "Customer",
        IsActive = true,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>
    /// The load-bearing assertion. Two accounts sharing one email is the
    /// failure this index exists to stop, and it is invisible above the
    /// database: both rows save cleanly, and the cost only shows up later —
    /// the customer cannot tell which account is theirs, and a password reset
    /// for that email becomes ambiguous.
    /// </summary>
    [Fact]
    public void TwoUsersCannotShareTheSameEmail()
    {
        _db.Users.Add(NewUser("first", "shared@x.local"));
        _db.SaveChanges();

        _db.Users.Add(NewUser("second", "shared@x.local"));

        Assert.Throws<DbUpdateException>(() => _db.SaveChanges());
    }

    /// <summary>
    /// Different emails must still work. Without this, "unique on Email" could
    /// be satisfied by a constraint that rejects every insert after the first,
    /// which would make the second test pass while registration was broken for
    /// everyone.
    /// </summary>
    [Fact]
    public void DifferentEmailsAreBothAccepted()
    {
        _db.Users.Add(NewUser("first", "one@x.local"));
        _db.Users.Add(NewUser("second", "two@x.local"));

        _db.SaveChanges();

        Assert.Equal(2, _db.Users.Count());
    }

    /// <summary>
    /// Usernames are unaffected. This is the pre-existing constraint that
    /// already protected this path; pinning it here means a future change to
    /// the Email index cannot quietly drop the Username one instead, which
    /// would be the same defect reached by a different route.
    /// </summary>
    [Fact]
    public void DuplicateUsernameIsStillRejected()
    {
        _db.Users.Add(NewUser("same", "one@x.local"));
        _db.SaveChanges();

        _db.Users.Add(NewUser("same", "two@x.local"));

        Assert.Throws<DbUpdateException>(() => _db.SaveChanges());
    }
}
