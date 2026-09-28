using Common.Data;
using Microsoft.EntityFrameworkCore;
using UserService.Data;
using UserService.DTOs;
using UserService.Models;
using UserService.Services;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// The concurrent case for the unique index on Users.Email, against a real
/// Postgres (issue #150).
///
/// WHY THIS IS NOT A SQLITE TEST, and why that is not a preference. A second
/// SqliteConnection writing while another holds the file produces
/// <c>SQLITE Error 5: database is locked</c> — a concurrency condition about
/// the file, carrying no information about the data. Measured on this branch,
/// that path throws the same <c>DbUpdateException</c> a unique violation
/// throws, so a SQLite test of this race cannot tell the two apart and would
/// pass while proving nothing. The provider that production runs on answers
/// the question on its own terms: a unique violation is SQLSTATE 23505, and a
/// lock wait is something else entirely.
///
/// WHY IT HAS TO BE REAL CONCURRENCY. ProvisionCustomerAccountAsync checks for
/// a duplicate with AnyAsync and then inserts in a separate statement. A test
/// that awaits the calls one after another only ever exercises the check, and
/// passes no matter whether the unique index exists — that version was written,
/// passed, and was then shown by mutation testing not to protect anything.
/// These two run under a barrier so both pass the check before either writes.
///
/// WHY IT MATTERS. CustomerService derives Username FROM Email, so the email
/// is what a customer account is created under. Two accounts sharing one means
/// the customer cannot tell which is theirs and a password reset for that
/// address is ambiguous. The losing request must be REFUSED, not throw: the
/// provisioner logs any failure and leaves the customer unlinked, so an
/// unhandled exception reads as an outage and sends an operator to the network
/// instead of to the row that already exists.
///
/// COLLECTION. Both classes that call Migrate() against EVM_TEST_POSTGRES sit
/// in <see cref="PostgresCollection"/>: on a fresh database they raced to
/// create __EFMigrationsHistory and one died on a duplicate key. The reason is
/// written up there, with the measured error, because it looks like a flake.
/// </summary>
[Collection("postgres")]
public class PostgresUserEmailRaceTests
{
    private readonly ITestOutputHelper _out;
    private readonly string _connection;

    public PostgresUserEmailRaceTests(ITestOutputHelper output)
    {
        _out = output;
        _connection = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)
            ?? throw new InvalidOperationException("unreachable: PostgresFact skips before the body runs");
    }

    private UserDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseNpgsql(_connection)
            .Options;
        var db = new UserDbContext(options);
        MigrationLock.Migrate(db);
        return db;
    }

    /// <summary>
    /// These tests write into the database the running stack already uses, so
    /// each one clears its own addresses first AND last. Cleaning only at the
    /// start would be enough for the run in progress, but a failing assertion
    /// skips the trailing cleanup and leaves rows behind — which is exactly
    /// what happened while developing this: the mutation run left 8 rows for
    /// one address and 4 for another, and the next run's CREATE UNIQUE INDEX
    /// then failed on data the test had written. The failure surfaced as a
    /// constraint error in an unrelated place, so the cleanup is in a finally
    /// rather than left to the happy path.
    /// </summary>
    private async Task ClearTestRowsAsync()
    {
        using var db = NewContext();
        await db.Users.Where(u =>
            u.Email == "race150@evm.local"
            || u.Email == "race150b@evm.local"
            || (u.Username != null && u.Username.StartsWith("race150"))).ExecuteDeleteAsync();
    }

    private static UserServiceImpl NewUserService(UserDbContext db) => new(
        db,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "issue-150-race-test-signing-key-0123456789",
                ["Jwt:Issuer"] = "evm.local",
                ["Jwt:Audience"] = "evm.local",
            })
            .Build(),
        new NoopEmail());

    private sealed class NoopEmail : IEmailService
    {
        public Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Two requests, one email, released together. Exactly one account must
    /// exist afterwards, and the other request must come back as a refusal.
    ///
    /// Eight requests rather than two: with a check-then-insert window, two is
    /// a coin flip that a slow CI machine can lose by scheduling them apart,
    /// and a test that only catches the race sometimes is worse than none.
    /// </summary>
    [PostgresFact]
    public async Task RacingRegistrationsOfOneEmailProduceExactlyOneAccount()
    {
        const string email = "race150@evm.local";
        await ClearTestRowsAsync();
        try
        {
            const int callers = 8;
            var gate = new TaskCompletionSource();

            // The barrier: every task waits here, so they are released
            // together and all of them run their duplicate check against the
            // same empty state before the first insert lands. Each task needs
            // its OWN context: EF contexts are not thread-safe, and sharing
            // one would serialise the calls and remove the very race this test
            // exists to create.
            var seenBeforeCheck = new System.Collections.Concurrent.ConcurrentBag<int>();
            var tasks = Enumerable.Range(0, callers).Select(async i =>
            {
                using var db = NewContext();
                var svc = NewUserService(db);
                await gate.Task;
                seenBeforeCheck.Add(await db.Users.CountAsync(u => u.Email == email));
                return await svc.ProvisionCustomerAccountAsync(
                    new CustomerAccountRequest(
                        $"race150-{i}@evm.local", email, $"Racer {i}", "Cust0mer!pass", 1));
            }).ToArray();

            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            using var verify = NewContext();
            var count = await verify.Users.CountAsync(u => u.Email == email);
            _out.WriteLine($"callers={callers}, succeeded={results.Count(r => r.Success)}, rows={count}");

            // The race actually happened. Without this, the assertions below
            // could pass for the wrong reason: if the tasks had been scheduled
            // one after another, every one of them would see the winner's row
            // and refuse on the AnyAsync check, and `count == 1` would be true
            // with the unique index doing nothing at all. Asserting that every
            // caller saw an empty table is what makes the index the thing
            // under test. Measured 8/8 across repeated runs before this was
            // turned into an assertion, so it is the observed behaviour and
            // not a hope about the scheduler.
            Assert.Equal(callers, seenBeforeCheck.Count(n => n == 0));

            // The load-bearing assertion. Every task ran the duplicate check,
            // so the unique index on Email is the only thing that can hold
            // this at 1.
            Assert.Equal(1, count);

            // And the losers are refusals, not exceptions — reaching this line
            // at all means none of them threw out of SaveChangesAsync.
            var refusals = results.Where(r => !r.Success).ToArray();
            Assert.Equal(callers - 1, refusals.Length);
            Assert.All(refusals, r => Assert.Null(r.UserId));
            Assert.NotNull(results.Single(r => r.Success).UserId);
        }
        finally
        {
            await ClearTestRowsAsync();
        }
    }

    /// <summary>
    /// A registration that loses the race must leave the winning account
    /// untouched, not modify or replace it. Asserting the count is 1 covers
    /// "no extra row"; this covers "the row that is there is the one that was
    /// created first", which a save that overwrote on conflict would pass on
    /// count alone.
    /// </summary>
    [PostgresFact]
    public async Task TheLosingRegistrationDoesNotDisturbTheWinningAccount()
    {
        const string email = "race150b@evm.local";
        await ClearTestRowsAsync();
        try
        {
            var gate = new TaskCompletionSource();
            var tasks = Enumerable.Range(0, 4).Select(async i =>
            {
                using var db = NewContext();
                var svc = NewUserService(db);
                await gate.Task;
                return await svc.ProvisionCustomerAccountAsync(
                    new CustomerAccountRequest(
                        $"race150b-{i}@evm.local", email, $"Racer {i}", "Cust0mer!pass", 1));
            }).ToArray();

            gate.SetResult();
            await Task.WhenAll(tasks);

            using var verify = NewContext();
            var rows = await verify.Users.Where(u => u.Email == email).ToListAsync();
            var winner = Assert.Single(rows);

            // The winner is a real, usable account — active, with a hash — not
            // a shell left by a half-applied conflict resolution.
            Assert.Equal("Customer", winner.Role);
            Assert.True(winner.IsActive);
            Assert.NotEmpty(winner.PasswordHash);
            Assert.StartsWith("race150b-", winner.Username);
        }
        finally
        {
            await ClearTestRowsAsync();
        }
    }
}
