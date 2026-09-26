using System.Security.Claims;
using System.Text;
using Common.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #92 (P2, follow-up): pins the short-lived service token that replaces
/// the static <c>InternalService__Key</c> shared secret.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS ALONGSIDE <see cref="InternalServiceAuthTests"/>
/// The static key is a bearer secret that never expires: it is in
/// <c>docker-compose.yml</c>, in <c>render.yaml</c> as <c>sync: false</c>, and in
/// five service dashboards, and on Render each of those services is its own
/// public web service. A copy that leaks stays live until someone rotates it
/// across all five at once. A signed token bounds that to a five-minute window
/// and stops the secret itself from being the credential.
///
/// WHAT MUST BE TRUE
/// 1. A fresh token promotes the principal, so <c>[Authorize]</c> stops
///    rejecting a machine call — this is the property whose absence caused the
///    silent empty-report bug.
/// 2. Every expiry and mis-signing rejection is silent at the caller: the
///    middleware still calls <c>next()</c> and adds no role, so authorization
///    produces 401. An exception here would be a 500 instead, which is a
///    different and louder failure.
/// 3. The role lives in the token, and a correctly-signed token WITHOUT it is
///    still rejected. Possession of the signing key must not by itself be
///    enough to become <c>InternalService</c>.
/// 4. Audience and issuer are pinned, so a token minted for another service
///    cannot be replayed here.
/// 5. While the static key is still configured and the deprecation flag is not
///    set, the old path keeps working. The rollout is staged, and a receiving
///    service that only gets the new code must not lock out the old caller.
/// </remarks>
public class InternalServiceTokenTests
{
    // 40 chars: comfortably over the 32-byte HMAC-SHA256 floor, so a test
    // failure means a real defect rather than an accidentally-short key.
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long-ok";
    private const string OtherKey = "a-completely-different-key-of-sufficient-length";
    private const string StaticKey = "the-old-static-key";

    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A token the middleware will accept. The middleware reads
    /// <c>DateTimeOffset.UtcNow</c> itself, so a token minted at the fixed
    /// <see cref="Now"/> is months stale by the time these run — minted here
    /// instead. Tests that need an expired token ask for one explicitly.
    /// </summary>
    private static string FreshToken() =>
        InternalServiceToken.Create(SigningKey, DateTimeOffset.UtcNow);

    private static IConfiguration Config(
        string? signingKey, bool requireToken = false, string? staticKey = StaticKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [InternalServiceToken.SigningKeyConfigPath] = signingKey,
                // Set by default because the staged-rollout property under test
                // is that BOTH credentials are accepted while the flag is unset.
                // With no static key configured the middleware declines to
                // promote anything and the test would pass for the wrong reason.
                [InternalServiceAuth.ConfigPath] = staticKey,
                [InternalServiceAuth.RequireSignedTokenConfigPath] = requireToken ? "true" : null,
            })
            .Build();

    private static async Task<HttpContext> RunAsync(
        IConfiguration configuration, string? presented)
    {
        var context = new DefaultHttpContext();
        if (presented is not null)
        {
            context.Request.Headers[InternalServiceAuth.HeaderName] = presented;
        }

        var nextCalled = false;
        var middleware = new InternalServiceAuthMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            configuration,
            NullLogger<InternalServiceAuthMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled,
            "the middleware must always call next(); it adds an identity, it does not terminate the pipeline");
        return context;
    }

    private static void AssertIsInternalService(HttpContext context) =>
        Assert.Contains(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);

    private static void AssertIsNotInternalService(HttpContext context) =>
        Assert.DoesNotContain(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);

    // ---------- the token itself ----------

    [Fact]
    public void ATokenRoundTrips_AndCarriesTheInternalRole()
    {
        var token = InternalServiceToken.Create(SigningKey, Now);

        Assert.True(InternalServiceToken.TryValidate(
            token, SigningKey, Now, out var principal));
        Assert.NotNull(principal);
        Assert.Contains(principal!.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);
    }

    [Fact]
    public void TheRoleIsReadableAsARole_NotOnlyAsSomeOtherClaimName()
    {
        // The static-key path writes ClaimTypes.Role and the static-key path is
        // what every [Authorize(Roles=...)] check is written against. Asserted
        // two ways on purpose: IsInRole is what the authorization pipeline calls,
        // and the claim type is what a mapping change would silently break
        // while IsInRole kept passing by coincidence.
        var token = InternalServiceToken.Create(SigningKey, Now);

        Assert.True(InternalServiceToken.TryValidate(token, SigningKey, Now, out var principal));

        Assert.Contains(principal!.Claims, c => c.Type == ClaimTypes.Role);
        Assert.True(principal.IsInRole(InternalServiceAuthMiddleware.InternalRole));
        Assert.Contains(principal.Claims,
            c => c.Type == ClaimTypes.Name && c.Value == InternalServiceToken.SubjectClaim);
    }

    [Fact]
    public void TheTokenPayloadCarriesTheShortRegisteredNames()
    {
        // Read back WITHOUT validation, to pin what actually goes on the wire.
        // The rest of this file can only see a token that validates; if Create
        // emitted the long ClaimTypes.Role URI as the claim name instead of
        // "role", every validation here would still pass and the token would
        // still be unreadable by any other IdentityModel implementation.
        var token = InternalServiceToken.Create(SigningKey, Now);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        var claimTypes = jwt.Claims.Select(c => c.Type).ToArray();

        Assert.Contains(InternalServiceToken.RoleClaim, claimTypes);
        Assert.Contains(InternalServiceToken.SubjectPayloadClaim, claimTypes);
        // iss/aud/exp are what TryValidate pins; if Create stopped emitting any
        // of them, validation would fail for a reason the other tests report as
        // a bare false.
        Assert.Contains("iss", claimTypes);
        Assert.Contains("aud", claimTypes);
        Assert.Contains("exp", claimTypes);
        Assert.Equal(InternalServiceToken.Issuer, jwt.Issuer);
        Assert.Equal(
            new[] { InternalServiceToken.Audience },
            jwt.Audiences.ToArray());
    }

    [Fact]
    public void AnExpiredToken_IsRejected()
    {
        // Minted already expired, with no clock help: validated at an instant
        // beyond LifetimeSeconds PLUS ClockSkewSeconds. Bounds the worst case
        // rather than just showing expiry works — a mutation that widened the
        // window, or dropped the skew from the expiry comparison, would still
        // pass a test that only went one second past exp.
        var token = InternalServiceToken.Create(SigningKey, Now);
        var beyondWindow = Now.AddSeconds(
            InternalServiceToken.LifetimeSeconds + InternalServiceToken.ClockSkewSeconds + 1);

        Assert.False(InternalServiceToken.TryValidate(token, SigningKey, beyondWindow, out _));
    }

    [Fact]
    public void ATokenInsideItsWindow_IsStillAccepted()
    {
        // The other half of the expiry test. Without this, "reject expired" is
        // satisfied by a validator that rejects everything, and the middleware
        // test above would be the only thing noticing.
        var token = InternalServiceToken.Create(SigningKey, Now);
        var justInside = Now.AddSeconds(InternalServiceToken.LifetimeSeconds);

        Assert.True(InternalServiceToken.TryValidate(token, SigningKey, justInside, out _));
    }

    [Fact]
    public void TheWindowIsExactlyLifetimePlusSkew()
    {
        // The two expiry tests above are self-referential: they build their
        // boundary out of the same constants Create uses, so widening
        // LifetimeSeconds moves the boundary with it and they still pass. This
        // one pins the real window by its edges instead -- valid on the last
        // second, invalid on the first second past it -- so a change to either
        // Create's window or the comparison is caught.
        var token = InternalServiceToken.Create(SigningKey, Now);
        var lastValid = Now.AddSeconds(
            InternalServiceToken.LifetimeSeconds + InternalServiceToken.ClockSkewSeconds);

        Assert.True(InternalServiceToken.TryValidate(token, SigningKey, lastValid, out _));
        Assert.False(InternalServiceToken.TryValidate(
            token, SigningKey, lastValid.AddSeconds(1), out _));
    }

    [Theory]
    [InlineData(300)]
    [InlineData(60)]
    public void TheWindowConstantsAreTheAgreedOnes(int seconds)
    {
        // The lifetime and the skew are a decision, not a tuning knob: five
        // minutes bounds a captured token, sixty seconds absorbs drift between
        // independently-deployed services. Both were chosen deliberately, so
        // changing either should break a build rather than silently widen or
        // narrow the security window.
        Assert.Contains(
            seconds,
            new[] { InternalServiceToken.LifetimeSeconds, InternalServiceToken.ClockSkewSeconds });
    }

    [Fact]
    public void TheMinimumKeySizeIsTheHmacSha256Floor()
    {
        // 256 bits, which is what HMAC-SHA256 requires (IDX10653 below it).
        // Lowering this is not a harmless loosening: the tests that feed a
        // "too short" key use 9 characters, so a floor dropped to 8 would let
        // them keep passing while real deployments started minting with a key
        // the crypto layer rejects at first use. Asserted on the constant
        // because every rejection test derives its input from it.
        Assert.Equal(32, InternalServiceToken.MinimumSigningKeyBytes);
        Assert.Equal(
            32,
            Encoding.UTF8.GetByteCount(
                "0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void ATokenSignedWithADifferentKey_IsRejected()
    {
        var token = InternalServiceToken.Create(SigningKey, Now);

        Assert.False(InternalServiceToken.TryValidate(token, OtherKey, Now, out _));
    }

    [Fact]
    public void ATokenWithAnAlteredPayload_IsRejected()
    {
        // Signature covers the payload, so editing any segment must fail even
        // though the token is still three well-formed base64url segments and
        // still decodes to a plausible JWT.
        var token = InternalServiceToken.Create(SigningKey, Now);
        var segments = token.Split('.');
        segments[1] = segments[1][..^4] +
            (segments[1][^4] == 'A' ? "BBBB" : "AAAA");

        Assert.False(InternalServiceToken.TryValidate(
            string.Join('.', segments), SigningKey, Now, out _));
    }

    [Fact]
    public void ATokenForAnotherAudience_IsRejected()
    {
        // Pins the audience check specifically. ValidIssuer/Audience are
        // non-default in TokenValidationParameters, but a mutation that swapped
        // ValidAudience for the issuer string would still produce a token that
        // passes every other assertion in this file.
        var foreign = Mint(audience: "some-other-service");

        Assert.False(InternalServiceToken.TryValidate(foreign, SigningKey, Now, out _));
    }

    [Fact]
    public void ATokenFromAnotherIssuer_IsRejected()
    {
        // The other half of the audience test. Issuer and audience are separate
        // parameters, and a mutation that pointed both at the same constant
        // would pass every other test in this file.
        var foreign = Mint(issuer: "some-other-issuer");

        Assert.False(InternalServiceToken.TryValidate(foreign, SigningKey, Now, out _));
    }

    [Fact]
    public void AValidlySignedTokenWithoutTheRole_IsRejected()
    {
        // Signature validity is necessary but not sufficient. The role is
        // checked inside TryValidate so that a token minted for some other
        // purpose under the same key cannot become InternalService.
        var roleless = Mint(includeRole: false);

        Assert.False(InternalServiceToken.TryValidate(roleless, SigningKey, Now, out _));
    }

    /// <summary>
    /// Builds a correctly-signed token with one field varied, so each rejection
    /// test isolates exactly one validation rather than failing for whichever
    /// reason happens to come first.
    /// </summary>
    private static string Mint(
        string issuer = InternalServiceToken.Issuer,
        string audience = InternalServiceToken.Audience,
        bool includeRole = true)
    {
        var claims = new List<Claim>
        {
            new(InternalServiceToken.SubjectPayloadClaim, InternalServiceToken.SubjectClaim),
        };
        if (includeRole)
        {
            claims.Add(new Claim(
                InternalServiceToken.RoleClaim, InternalServiceAuthMiddleware.InternalRole));
        }

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }
            .CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Subject = new ClaimsIdentity(claims),
                // NotBefore must be set explicitly alongside Expires: left at
                // its default of DateTime.UtcNow, it lands in 2026-09 while Now
                // is 2026-03, and the handler refuses the pair outright
                // (IDX12401) before any of this test's assertions are reached.
                NotBefore = Now.UtcDateTime,
                Expires = Now.UtcDateTime.AddMinutes(5),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    SecurityAlgorithms.HmacSha256),
            });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public void GarbageInTheTokenSlot_IsRejectedWithoutThrowing(string? presented)
    {
        // The middleware runs on a request path serving a PUBLIC url. A
        // malformed header must be an ordinary rejection, never an unhandled
        // exception that turns a 401 into a 500.
        Assert.False(InternalServiceToken.TryValidate(presented, SigningKey, Now, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void AnUnusableSigningKey_ValidatesNothing(string? signingKey)
    {
        // A receiver with no usable key must not accept anything, including a
        // perfectly valid token. The static-key middleware already had this
        // property; the token path has to keep it.
        var token = InternalServiceToken.Create(SigningKey, Now);

        Assert.False(InternalServiceToken.TryValidate(token, signingKey, Now, out _));
    }

    [Fact]
    public void MintingWithAnUnusableKey_ThrowsWithAReadableMessage()
    {
        var ex = Assert.Throws<InternalServiceToken.InvalidServiceTokenException>(
            () => InternalServiceToken.Create("short", Now));

        // The message has to name the setting, or a misconfigured deploy is a
        // stack trace to hunt through rather than a line of log.
        Assert.Contains(InternalServiceToken.SigningKeyConfigPath, ex.Message);
    }

    [Fact]
    public void TryValidate_NeverThrows_ForAnyHostileInput()
    {
        // The stated contract is stronger than "returns false for the cases we
        // thought of": it never throws, full stop. The catch block inside
        // TryValidate is what upholds that, and it is unreachable by the inputs
        // below -- measured, not assumed. Every one of them returns IsValid
        // from the validator without an exception, so nothing here exercises
        // the catch, and this test is NOT evidence that the catch works.
        //
        // What it does protect is the observable half: if a future
        // Microsoft.IdentityModel version starts throwing on one of these
        // shapes, this goes red at the point of change rather than the 500
        // showing up on a public endpoint in production. The claim-shaped
        // cases are listed deliberately -- an array or a number where a string
        // is expected is exactly what a future version is most likely to start
        // type-checking -- so the test covers the shapes worth watching rather
        // than a random pile of strings.
        var hostiles = new (string Label, string Token)[]
        {
            ("aud is an array", Sign(WithAud("[\"ev-dealer-internal\"]"))),
            ("aud is a number", Sign(WithAud("7"))),
            ("aud is an empty array", Sign(WithAud("[]"))),
            ("iss is an array", Sign(WithIss("[\"ev-dealer-reporting\"]"))),
            ("payload is not an object", Sign("[]")),
            ("base64 with padding", "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJ9=." + "x="),
            ("base64 with plus", "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJ9+.x"),
            ("base64 with slash", "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJ9/.x"),
            ("replacement character", "a.b.c�"),
            ("null byte", "ab\0c"),
            ("four segments", "a.b.c.d"),
            ("100k characters", new string('a', 100000)),
        };

        foreach (var (label, token) in hostiles)
        {
            // No exception is the assertion. Assert.False alone would pass if
            // TryValidate threw its way out of a bad input.
            Assert.False(
                InternalServiceToken.TryValidate(token, SigningKey, Now, out _),
                $"expected a clean rejection for: {label}");
        }

        // A key too short for HMAC-SHA256 is the other class the public
        // surface can be handed, and it is rejected by the guard before the
        // try block rather than by the validator.
        Assert.False(InternalServiceToken.TryValidate(FreshToken(), "short", Now, out _));
    }

    /// <summary>
    /// The claim set a valid token carries: correct issuer, audience, subject,
    /// role and a live window. Written out by hand rather than minted, so a
    /// test can replace exactly one claim and leave the rest valid. Minting it
    /// instead would mean a rejection could always be blamed on the signature,
    /// and these tests would prove nothing.
    /// </summary>
    private static string ValidClaims =>
        "{\"iss\":\"" + InternalServiceToken.Issuer + "\""
        + ",\"aud\":\"" + InternalServiceToken.Audience + "\""
        + ",\"sub\":\"" + InternalServiceToken.SubjectClaim + "\""
        + ",\"role\":\"" + InternalServiceAuthMiddleware.InternalRole + "\""
        + ",\"nbf\":" + Now.ToUnixTimeSeconds()
        + ",\"exp\":" + Now.AddSeconds(InternalServiceToken.LifetimeSeconds).ToUnixTimeSeconds() + "}";

    private static string WithAud(string rawJson) => ValidClaims.Replace(
        "\"aud\":\"" + InternalServiceToken.Audience + "\"",
        "\"aud\":" + rawJson, StringComparison.Ordinal);

    private static string WithIss(string rawJson) => ValidClaims.Replace(
        "\"iss\":\"" + InternalServiceToken.Issuer + "\"",
        "\"iss\":" + rawJson, StringComparison.Ordinal);

    /// <summary>
    /// Base64url-encodes a payload and signs it with <see cref="SigningKey"/>,
    /// so a claim shape the minting API cannot express can still reach the
    /// validator exactly as a holder of the key would send it.
    /// </summary>
    private static string Sign(string payloadJson)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var head = B64("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var body = B64(payloadJson);
        var signature = Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SigningKey), Encoding.ASCII.GetBytes($"{head}.{body}")));

        return $"{head}.{body}.{B64(signature)}";
    }

    // ---------- the middleware on the receiving side ----------

    [Fact]
    public async Task AValidToken_PromotesThePrincipal()
    {
        var context = await RunAsync(Config(SigningKey), FreshToken());

        AssertIsInternalService(context);
    }

    [Fact]
    public async Task AnExpiredToken_LeavesThePrincipalAnonymous()
    {
        // The middleware calls DateTimeOffset.UtcNow itself, so this mints a
        // token that expired an hour ago rather than trying to control time.
        var context = await RunAsync(Config(SigningKey),
            InternalServiceToken.Create(SigningKey, DateTimeOffset.UtcNow.AddHours(-1)));

        AssertIsNotInternalService(context);
    }

    [Fact]
    public async Task AStaticKey_StillWorks_WhileTheFlagIsUnset()
    {
        // The rollout is staged. A receiving service running the new code must
        // keep accepting the old credential until the deprecation flag is turned
        // on, or deploying receivers first breaks the fan-out for the whole
        // window between the two deploys.
        var context = await RunAsync(Config(SigningKey), StaticKey);

        AssertIsInternalService(context);
    }

    [Fact]
    public async Task AStaticKey_IsRejected_OnceTheFlagIsSet()
    {
        // The point of the flag: this is how the static path is finally turned
        // off without a second coordinated deploy.
        var context = await RunAsync(Config(SigningKey, requireToken: true), StaticKey);

        AssertIsNotInternalService(context);
    }

    [Fact]
    public async Task AValidToken_StillWorks_OnceTheFlagIsSet()
    {
        // Otherwise "the flag rejects the old key" is satisfied by a flag that
        // rejects everything, and the rollout would end with the fan-out dead.
        var context = await RunAsync(Config(SigningKey, requireToken: true), FreshToken());

        AssertIsInternalService(context);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AReceiverWithNoStaticKeyConfigured_PromotesNobody(string? staticKey)
    {
        // Found by mutation, not by reading. Making the middleware's empty-key
        // guard a no-op left all 303 tests green, and so did dropping just its
        // early return. The property "a receiver with no shared secret promotes
        // nobody" was therefore asserted nowhere, even though the code comment
        // above that guard states it as a deliberate design decision.
        //
        // The guard itself is redundant today: FixedTimeEquals returns false for
        // an empty expected, so the same rejection happens one line later. What
        // this pins is the property rather than the redundancy -- the guard and
        // the helper are two independent things that have to keep agreeing, and
        // a future helper written as "two nulls are equal" is a perfectly
        // reasonable-looking change that would silently promote anyone sending
        // the header to a receiver that never had a key.
        var context = await RunAsync(
            Config(SigningKey, staticKey: staticKey), "anything-at-all");

        AssertIsNotInternalService(context);
    }

    [Fact]
    public async Task AReceiverWithNoSigningKey_RejectsEvenAValidToken()
    {
        // Guards the half-deployed state: receivers are updated before the
        // caller is, so a receiver whose signing key has not been filled in yet
        // must fail closed rather than accept the static key because it happens
        // to still be configured.
        var context = await RunAsync(Config(signingKey: null), FreshToken());

        AssertIsNotInternalService(context);
    }

    [Fact]
    public async Task AReceiverWithNoSigningKey_StillRejectsTheStaticKey_WhenTheFlagIsSet()
    {
        // The mirror of the half-deployed state, and the one that matters at
        // the end of the rollout: a receiver whose signing key was never filled
        // in must not quietly keep serving the static credential just because
        // the key still happens to be in configuration.
        var context = await RunAsync(
            Config(signingKey: null, requireToken: true), StaticKey);

        AssertIsNotInternalService(context);
    }
}
