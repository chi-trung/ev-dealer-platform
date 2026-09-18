using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #41: pin the "dealer" claim that UserService.LoginAsync mints into
/// every login JWT. The whole Issue #36 dealer-subject flow (device-token
/// registration for dealer:&lt;n&gt;) depends on this claim existing at login;
/// the DeviceTokens auth tests only cover the CONTROLLER's decision table
/// given a claim, never the minting side — so dropping the claim block in
/// Program.cs left the suite green while silently breaking #36 for real users.
///
/// Unlike the extract-a-helper option the issue suggested, this exercises the
/// REAL UserServiceImpl.LoginAsync end to end: seed a User row in a temp
/// SQLite UserDbContext, log in with the correct password, then assert on the
/// claims the returned JWT actually carries. That pins the full path —
/// claim list → SecurityTokenDescriptor → signed token — not just a pure
/// helper someone could keep green while the wiring around it rots.
///
/// The token payload is read by BASE64URL-DECODING the middle JWT segment
/// straight to JSON, NOT via JwtSecurityTokenHandler.ReadJwtToken().Claims.
/// Two reasons. (1) Design: the raw payload IS the wire bytes a #36 consumer
/// receives, so decoding it pins those bytes with zero dependency on any
/// reader's inbound claim-mapping. (2) In THIS build the legacy read-back
/// deterministically drops claims: probed 3/3 runs, a 7.1.2 JwtSecurityTokenHandler
/// surfaced only id/role/nbf/iat/aud while the raw payload (and the modern
/// 8.x JsonWebTokenHandler) carry all nine members — dealer, unique_name, exp
/// and iss included. The cause is a version skew this test project inherits:
/// referencing UserService (JwtBearer 8.x) resolves Microsoft.IdentityModel
/// Tokens/JsonWebTokens to 8.14.0 while System.IdentityModel.Tokens.Jwt stays
/// 7.1.2. A standalone probe on a clean single-version 7.1.2 graph does NOT
/// drop them, so the handler isn't universally lossy — but on THIS graph a
/// .Claims-based assertion would under-report the wire and could HIDE a
/// dropped "dealer" claim, the exact regression this file exists to catch.
/// Note the outbound wire names are the short forms: ClaimTypes.Name ->
/// "unique_name", ClaimTypes.Role -> "role" (JwtSecurityTokenHandler's claim
/// map); an ASP.NET JwtBearer reader maps them back to ClaimTypes.* in the
/// controller, so a #36 consumer still sees User.FindFirst(ClaimTypes.Name).
/// Here we pin the wire.
/// </summary>
[Collection("sqlite")]
public class DealerClaimLoginTests : IDisposable
{
    // Any HS256 key works — we only READ the token back (claims are not
    // encrypted, and we are not validating the signature against a trusted
    // issuer here; the point is the payload's claim set).
    private const string JwtKey = "issue-41-test-signing-key-0123456789abcdef";
    private const string Password = "S3cret!correct";

    private readonly string _dbPath;
    private readonly DbContextOptions<UserDbContext> _options;

    public DealerClaimLoginTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dealer_claim_{Guid.NewGuid():N}.db");
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

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = "evm.local",
            ["Jwt:Audience"] = "evm.local",
        })
        .Build();

    private async Task SeedUserAsync(int id, string username, int? dealerId, bool active = true)
    {
        using var db = new UserDbContext(_options);
        // Issue #121: UserService no longer maps the Dealers table, so
        // User.DealerId is not a DB FK here — seed the user row directly
        // with no prerequisite dealer row.
        db.Users.Add(new User
        {
            Id = id,
            Username = username,
            Email = $"{username}@example.com",
            FullName = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            Role = "DealerStaff",
            IsActive = active,
            DealerId = dealerId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Logs in for real and returns the JWT payload as a raw JSON
    /// document (the exact bytes between the token's first two dots), so the
    /// assertions below see the wire format with no handler mapping applied.
    /// The payload is not signed-verified here — see the JwtKey comment.</summary>
    private async Task<JsonDocument> LoginAndDecodePayload(string username)
    {
        using var db = new UserDbContext(_options);
        var svc = new UserServiceImpl(db, Config(), new NoopEmail());
        var result = await svc.LoginAsync(new LoginRequest(username, Password));

        Assert.True(result.Success, "login should succeed for seeded active user");
        Assert.NotNull(result.Token);

        var segment = result.Token!.Split('.')[1];
        var b64 = segment.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
    }

    /// <summary>Value of a top-level payload member, or null when the member
    /// is absent — the "omitted, not empty" contract is a TryGetRootProperty
    /// check, so a present-but-null claim also fails these pins.</summary>
    private static string? PayloadClaim(JsonDocument payload, string type) =>
        payload.RootElement.TryGetProperty(type, out var v) ? v.ToString() : null;

    private static bool HasPayloadClaim(JsonDocument payload, string type) =>
        payload.RootElement.TryGetProperty(type, out _);

    [Fact]
    public async Task Login_DealerStaff_EmitsDealerClaimWithValue()
    {
        await SeedUserAsync(id: 7, username: "staff-with-dealer", dealerId: 42);
        using var payload = await LoginAndDecodePayload("staff-with-dealer");

        // THE pin: #36's device-token registry resolves dealer:<n> from this.
        Assert.Equal("42", PayloadClaim(payload, "dealer"));
        // The three always-present claims the minting block guarantees; the
        // controller reads "id" to authorize own-subject registration. Name
        // and role appear in their short wire forms (see class summary).
        Assert.Equal("7", PayloadClaim(payload, "id"));
        Assert.Equal("staff-with-dealer", PayloadClaim(payload, "unique_name"));
        Assert.Equal("DealerStaff", PayloadClaim(payload, "role"));
    }

    [Fact]
    public async Task Login_UserWithoutDealer_OmitsDealerClaimEntirely()
    {
        // Issue #36's Program.cs comment is explicit: the claim is OMITTED,
        // not present-but-empty, when DealerId is null — a consumer parsing
        // int must fail closed, and "" would parse differently than absent.
        await SeedUserAsync(id: 8, username: "staff-no-dealer", dealerId: null);
        using var payload = await LoginAndDecodePayload("staff-no-dealer");

        Assert.False(HasPayloadClaim(payload, "dealer"));
        // Still gets the identity claims.
        Assert.Equal("8", PayloadClaim(payload, "id"));
    }

    // NOTE: no "DealerId == 0 still emits" case. The minting guard is
    // user.DealerId.HasValue, and rewriting it to user.DealerId > 0 is the
    // only way an int-0 would change behavior — but no logged-in user can
    // have DealerId 0: User.DealerId is an FK to Dealers.Id, an integer
    // primary key whose generated values start at 1 (SQLite rowid under EF's
    // identity convention), so 0 is not a representable value. The
    // HasValue-vs->0 distinction is therefore unobservable through the real
    // LoginAsync path (seeding it would require raw SQL to fake an unreachable
    // row). Tests above already pin the two OBSERVABLE branches: present id →
    // claim emitted; null id → claim omitted.

    [Fact]
    public async Task Login_InactiveUser_FailsAndEmitsNoToken()
    {
        // Guards the "no token on failure" path the dealer claim depends on:
        // an unapproved account never receives a claim-bearing JWT.
        await SeedUserAsync(id: 10, username: "pending", dealerId: 5, active: false);
        using var db = new UserDbContext(_options);
        var svc = new UserServiceImpl(db, Config(), new NoopEmail());

        var result = await svc.LoginAsync(new LoginRequest("pending", Password));

        Assert.False(result.Success);
        Assert.Null(result.Token);
    }

    private sealed class NoopEmail : IEmailService
    {
        public Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink) => Task.CompletedTask;
    }
}
