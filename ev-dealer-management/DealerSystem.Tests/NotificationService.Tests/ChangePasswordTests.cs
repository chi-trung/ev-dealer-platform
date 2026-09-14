using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #50: the Settings page "Đổi mật khẩu" form POSTs
/// /auth/change-password — a route UserService never mapped, so every real
/// attempt 404'd (the gateway proxies /api/auth/{everything} and the service
/// answered nothing). These tests run the REAL UserServiceImpl.ChangePasswordAsync
/// against a temp SQLite UserDbContext, the same harness DealerClaimLoginTests
/// established: seeding through EF and asserting through EF pins the whole
/// path (verify → hash → persist → token invalidation), not a helper.
///
/// Mutation checks:
/// - drop the BCrypt.Verify(current) branch → WrongCurrentPassword fails
///   (and its hash-unchanged assert proves no write leaked first);
/// - return success without assigning PasswordHash → CorrectPassword test
///   fails on the re-verify;
/// - remove the live-token invalidation loop → OutstandingResetToken test
///   fails (the token would survive a password change, keeping a leaked
///   forgot-password link usable for its remaining hour);
/// - remove the length floor → ShortNewPassword fails; the floor matches
///   ResetPasswordAsync's <6 exactly so the two paths can't drift.
/// </summary>
public class ChangePasswordTests : IDisposable
{
    private const string OldPassword = "S3cret!old-here";
    private const string NewPassword = "N3w!password-here";

    private readonly string _dbPath;
    private readonly DbContextOptions<UserDbContext> _options;

    public ChangePasswordTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"chpwd_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var db = new UserDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    private sealed class NoopEmail : IEmailService
    {
        public Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink) => Task.CompletedTask;
    }

    private async Task<User> SeedUserAsync(bool active = true)
    {
        using var db = new UserDbContext(_options);
        var user = new User
        {
            Username = "settings-owner",
            Email = "owner@example.com",
            FullName = "Owner",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(OldPassword),
            Role = "Staff",
            IsActive = active,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<PasswordResetResult> ChangeAsync(int userId, string current, string @new)
    {
        using var db = new UserDbContext(_options);
        var svc = new UserServiceImpl(db, Config(), new NoopEmail());
        return await svc.ChangePasswordAsync(userId, new ChangePasswordRequest(current, @new));
    }

    private async Task<User> ReloadAsync(int userId)
    {
        using var db = new UserDbContext(_options);
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId))!;
    }

    [Fact]
    public async Task CorrectCurrentPassword_ChangesHash_VerifiesWithNewPassword()
    {
        var seeded = await SeedUserAsync();

        var result = await ChangeAsync(seeded.Id, OldPassword, NewPassword);

        Assert.True(result.Success);
        var user = await ReloadAsync(seeded.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify(NewPassword, user.PasswordHash),
            "the stored hash must authenticate the NEW password");
        Assert.False(BCrypt.Net.BCrypt.Verify(OldPassword, user.PasswordHash),
            "the OLD password must stop working — a hash assignment that no-ops passes the first assert");
    }

    [Fact]
    public async Task WrongCurrentPassword_RejectsAndLeavesHashUntouched()
    {
        var seeded = await SeedUserAsync();
        var before = (await ReloadAsync(seeded.Id)).PasswordHash;

        var result = await ChangeAsync(seeded.Id, "totally-wrong", NewPassword);

        Assert.False(result.Success);
        Assert.Equal(before, (await ReloadAsync(seeded.Id)).PasswordHash);
    }

    [Fact]
    public async Task NewPasswordUnderSixChars_Rejects_SameFloorAsResetFlow()
    {
        // ResetPasswordAsync enforces <6; this path must not drift from it.
        var seeded = await SeedUserAsync();

        var result = await ChangeAsync(seeded.Id, OldPassword, "12345");

        Assert.False(result.Success);
        Assert.True(BCrypt.Net.BCrypt.Verify(OldPassword, (await ReloadAsync(seeded.Id)).PasswordHash));
    }

    [Theory]
    [InlineData("", "N3w!password-here")]   // no current proof
    [InlineData("   ", "N3w!password-here")] // whitespace is not a password
    [InlineData("S3cret!old-here", "")]      // empty new password
    [InlineData("S3cret!old-here", "    ")]
    public async Task BlankInputs_Reject(string current, string @new)
    {
        var seeded = await SeedUserAsync();
        var result = await ChangeAsync(seeded.Id, current, @new);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task UnknownOrInactiveUser_Rejects()
    {
        var inactive = await SeedUserAsync(active: false);

        Assert.False((await ChangeAsync(inactive.Id, OldPassword, NewPassword)).Success);
        Assert.False((await ChangeAsync(9999, OldPassword, NewPassword)).Success);
        // the inactive account must still own its old hash
        Assert.True(BCrypt.Net.BCrypt.Verify(OldPassword, (await ReloadAsync(inactive.Id)).PasswordHash));
    }

    [Fact]
    public async Task OutstandingResetToken_IsInvalidatedByPasswordChange()
    {
        // The issue asked explicitly about "reset-in-flight". A live
        // forgot-password token needs ONLY the token to set a new password —
        // if it survived the change, a leaked/abandoned link would silently
        // re-open the account for its remaining hour after the user proved
        // they own the password.
        var seeded = await SeedUserAsync();
        using (var db = new UserDbContext(_options))
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = seeded.Id,
                Token = "live-token-abc",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
            });
            await db.SaveChangesAsync();
        }

        Assert.True((await ChangeAsync(seeded.Id, OldPassword, NewPassword)).Success);

        using (var db = new UserDbContext(_options))
        {
            var t = await db.PasswordResetTokens.SingleAsync(x => x.Token == "live-token-abc");
            Assert.True(t.IsUsed);
            Assert.NotNull(t.UsedAt);
        }
    }

    [Fact]
    public async Task ExpiredOrUsedTokens_AreNotRewritten()
    {
        // The invalidation query filters (!IsUsed && ExpiresAt > now); already
        // dead rows stay dead without a pointless UPDATE, and a token that
        // would never validate stays untouched (UsedAt history preserved).
        var seeded = await SeedUserAsync();
        using (var db = new UserDbContext(_options))
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = seeded.Id, Token = "expired-xyz",
                ExpiresAt = DateTime.UtcNow.AddHours(-1), CreatedAt = DateTime.UtcNow.AddDays(-2),
            });
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = seeded.Id, Token = "used-xyz",
                ExpiresAt = DateTime.UtcNow.AddHours(1), IsUsed = true,
                UsedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        Assert.True((await ChangeAsync(seeded.Id, OldPassword, NewPassword)).Success);

        using (var db = new UserDbContext(_options))
        {
            Assert.False((await db.PasswordResetTokens.SingleAsync(x => x.Token == "expired-xyz")).IsUsed);
            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                (await db.PasswordResetTokens.SingleAsync(x => x.Token == "used-xyz")).UsedAt);
        }
    }

    [Fact]
    public async Task RequestRecord_BindsExactFrontendCamelCaseBody()
    {
        // The route lives in Program.cs alongside its siblings (the 404 was
        // the bug itself — existence is pinned by it compiling + being wired;
        // the auth [Authorize] attribute mirrors /api/users/me exactly). What
        // a rename WOULD break silently is the body binding: Settings.jsx
        // posts {currentPassword, newPassword}. A record rename without the
        // matching frontend change (or vice versa) lands both fields null →
        // blank-input rejection; this test pins the contract.
        var req = System.Text.Json.JsonSerializer.Deserialize<ChangePasswordRequest>(
            """{ "currentPassword": "a", "newPassword": "bb" }""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal("a", req.CurrentPassword);
        Assert.Equal("bb", req.NewPassword);
        await Task.CompletedTask;
    }
}
