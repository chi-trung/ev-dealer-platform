using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Common.Auth;

/// <summary>
/// Short-lived service-to-service tokens: minting on the calling side,
/// verifying on the receiving side. The replacement for the static
/// <c>InternalService__Key</c> shared secret, kept under the same header so
/// <c>StripInternalServiceKeyMiddleware</c> at the gateway stays meaningful.
/// </summary>
/// <remarks>
/// WHY A STATIC KEY IS NOT ENOUGH
/// A shared secret is valid until someone rotates it. The key lives in
/// <c>docker-compose.yml</c>, in <c>render.yaml</c> as <c>sync: false</c>, and in
/// five service dashboards — so a copy that leaks is live forever, and
/// revoking it means a coordinated redeploy of all five. On Render each of
/// those services is its own public web service (verified: the blueprint has no
/// isolation mechanism, see the Render API findings in the P2 PR), so the key is
/// presented to the open internet rather than over a private network.
///
/// A signed token narrows the damage to a window instead of an unbounded one:
/// whoever captures one gets at most <see cref="LifetimeSeconds"/> of access,
/// and the same secret is no longer itself the bearer credential.
///
/// THE KEY IS NOT THE CREDENTIAL ANY MORE
/// The bearer value is now the token, and the shared secret becomes a signing
/// key that never leaves the servers. That matters for the fan-out: a key
/// captured from SalesService can no longer be replayed as a bearer header, and
/// because the audience is pinned, it cannot be replayed against a different
/// service either — a token minted for <see cref="Audience"/> fails
/// <c>ValidateAudience</c> anywhere else.
///
/// WHY THESE CLAIMS
/// <list type="bullet">
/// <item><c>sub</c> — which service minted it, for logs when a call is rejected.</item>
/// <item><c>role=InternalService</c> — the same role the static-key path grants,
/// so <c>[Authorize(Roles = InternalService)]</c> and the existing
/// <see cref="InternalServiceAuthMiddleware.InternalRole"/> checks work
/// unchanged against either credential.</item>
/// <item><c>aud</c> — see above.</item>
/// <item><c>exp</c>/<c>nbf</c> — the window itself.</item>
/// </list>
///
/// NOT VERIFIED HERE
/// A token minted by the reporting service cannot be distinguished from one
/// minted by any other holder of the signing key. Closing that would need
/// per-service keys, which the plan explicitly rejected as more config than the
/// risk warrants. What this does buy is expiry and separation of the bearer
/// credential from the shared secret.
///
/// WHY JsonWebTokenHandler AND NOT JwtSecurityTokenHandler
/// Written against Microsoft.IdentityModel.JsonWebTokens deliberately, with no
/// version pinned (see Common.csproj). The seven services do not resolve the
/// same Microsoft.IdentityModel.* versions: six land on 7.1.2 via
/// Microsoft.AspNetCore.Authentication.JwtBearer 8.0.10, while CustomerService
/// lands on 8.14.0 via AutoMapper 15.1.3. A split graph -- 7.1.2 and 8.14.0
/// loaded into one process -- was measured to make the payload reader return
/// only sub/nbf/iat/aud, silently dropping iss, role and exp, so every token
/// fails with IDX10211. Either version on its own behaves correctly. The
/// version-independent API is the one that is correct on both, and pinning a
/// version here would be what creates the split.
/// </remarks>
public static class InternalServiceToken
{
    /// <summary>
    /// Config path for the HMAC signing key. Deliberately NOT
    /// <c>Jwt:Key</c>: that one already signs and validates human tokens across
    /// all seven services, so sharing it would mean rotating the service signing
    /// key and the user JWT key are the same event.
    /// </summary>
    public const string SigningKeyConfigPath = "InternalService:SigningKey";

    /// <summary>Who mints these. Pinned so a token from elsewhere is rejected.</summary>
    public const string Issuer = "ev-dealer-reporting";

    /// <summary>
    /// Who accepts these. Pinned for the same reason: a token minted for a
    /// different audience must not be accepted here.
    /// </summary>
    public const string Audience = "ev-dealer-internal";

    /// <summary>Claim naming the minting service, for rejection logs.</summary>
    public const string SubjectClaim = "reporting";

    /// <summary>
    /// Payload key for the role, written and read as this exact string.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <see cref="ClaimTypes.Role"/>. That constant is the long
    /// URI <c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c>,
    /// and writing it into a token payload ships that URL as the claim name.
    /// Measured on both 7.1.2 and 8.14.0: a token built from a
    /// <c>Claim(ClaimTypes.Role, ...)</c> comes back with the URI as its claim
    /// type, so a reader looking for "role" finds nothing and IsInRole is False.
    /// The short name is the registered JWT one and is what the payload must
    /// carry; <see cref="TryValidate"/> maps it back to
    /// <see cref="ClaimTypes.Role"/> on the way out, which is what
    /// <c>[Authorize(Roles = ...)]</c> actually matches against.
    /// </remarks>
    public const string RoleClaim = "role";

    /// <summary>Payload key for the subject. The registered JWT short name.</summary>
    public const string SubjectPayloadClaim = "sub";

    /// <summary>
    /// Authentication type stamped on the principal
    /// <see cref="TryValidate"/> builds. Distinct from the static-key path's
    /// <c>InternalServiceKey</c> so logs and diagnostics can tell which
    /// credential authenticated a request.
    /// </summary>
    public const string AuthenticationType = "InternalServiceToken";

    /// <summary>
    /// How long a token is valid. 5 minutes: long enough for one fan-out with
    /// retry headroom, short enough that a captured token is near-useless by the
    /// time anyone notices the leak.
    /// </summary>
    public const int LifetimeSeconds = 300;

    /// <summary>
    /// Tolerance applied to <c>nbf</c>/<c>exp</c>, absorbing clock drift
    /// between services. Applied on top of <see cref="LifetimeSeconds"/>, so the
    /// real worst-case window is 6 minutes, not 5.
    /// </summary>
    public const int ClockSkewSeconds = 60;

    /// <summary>
    /// HMAC-SHA256 requires a key of at least 256 bits. A shorter one is not
    /// silently padded — <c>SymmetricSecurityKey</c> throws IDX10653 at first
    /// use, which on a receiving service would surface as every fan-out call
    /// failing with an opaque crypto error. This turns it into a clear message.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>
    /// Raised when a token cannot be trusted. Carries no crypto detail: the
    /// failure modes (wrong signature, wrong audience, expired) must not be
    /// distinguishable to a caller probing the public endpoint, and the token
    /// itself is never included.
    /// </summary>
    public sealed class InvalidServiceTokenException : Exception
    {
        public InvalidServiceTokenException(string message) : base(message) { }
        public InvalidServiceTokenException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// True when a signing key is usable. Checked at boot rather than at first
    /// use so a misconfigured deploy fails immediately and visibly.
    /// <see cref="NotNullWhenAttribute"/> states the null half of that verdict,
    /// so a caller that has already checked does not have to re-assert it to
    /// satisfy the compiler.
    /// </summary>
    public static bool IsUsableSigningKey([NotNullWhen(true)] string? key) =>
        !string.IsNullOrWhiteSpace(key) && Encoding.UTF8.GetByteCount(key) >= MinimumSigningKeyBytes;

    /// <summary>
    /// Mints a token for the reporting fan-out. <paramref name="now"/> is a
    /// parameter rather than a call to <see cref="DateTimeOffset.UtcNow"/> so
    /// tests can mint an already-expired token deterministically.
    /// </summary>
    public static string Create(string signingKey, DateTimeOffset now)
    {
        if (!IsUsableSigningKey(signingKey))
        {
            throw new InvalidServiceTokenException(
                $"{SigningKeyConfigPath} must be at least {MinimumSigningKeyBytes} bytes for HMAC-SHA256");
        }

        var issued = now.UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(SubjectPayloadClaim, SubjectClaim),
                new Claim(RoleClaim, InternalServiceAuthMiddleware.InternalRole),
            }),
            NotBefore = issued,
            Expires = issued.AddSeconds(LifetimeSeconds),
            IssuedAt = issued,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
        };

        // SetDefaultTimesOnTokenCreation off because every time claim is set
        // explicitly above from `now`. Left on, the handler fills in whatever
        // DateTime.UtcNow is on top of them, which would make the `now`
        // argument a lie for a token minted in the past -- exactly what the
        // expiry tests rely on being able to do.
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }
            .CreateToken(descriptor);
    }

    /// <summary>
    /// Verifies a token and returns the principal it asserts. The reference
    /// instant is a parameter, not a call to <see cref="DateTimeOffset.UtcNow"/>
    /// inside the method, so tests can pin "now" and the validation is a pure
    /// function of its inputs.
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> — never throws — for every failure mode: bad
    /// signature, wrong issuer, wrong audience, expired, not yet valid,
    /// malformed, or a correctly-signed token that omits the internal role.
    /// Callers are on a request path where any of those is an ordinary outcome
    /// (a probe, a stale token, a misconfiguration), and an exception there
    /// would turn a rejected call into a 500.
    ///
    /// The role check is inside this method rather than left to the caller
    /// because a token is only a service credential if it says it is one:
    /// possession of the signing key alone must not be enough, so a token minted
    /// without the role is rejected here even though its signature is valid.
    /// </remarks>
    public static bool TryValidate(
        string? token, string? signingKey, DateTimeOffset now, out ClaimsPrincipal? principal)
    {
        principal = null;

        if (string.IsNullOrWhiteSpace(token) || !IsUsableSigningKey(signingKey))
        {
            return false;
        }

        try
        {
            var result = new JsonWebTokenHandler().ValidateTokenAsync(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(signingKey)),
                    RequireSignedTokens = true,
                    // Lifetime is the entire point of this type, so it is not
                    // optional -- an unexpired-checked token would make every
                    // other improvement here cosmetic.
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(ClockSkewSeconds),
                    // Load-bearing, not a test convenience: without this the
                    // handler falls back to DateTime.UtcNow internally and the
                    // `now` parameter above is silently ignored. A test that
                    // pinned `now` in the past would then pass or fail for
                    // reasons that have nothing to do with the token, and an
                    // already-expired token would still be rejected correctly
                    // by accident rather than by the window this type is built
                    // around.
                    LifetimeValidator = (notBefore, expires, tokenParameter, parameters) =>
                    {
                        var reference = now.UtcDateTime;
                        return (notBefore is null || notBefore <= reference + parameters.ClockSkew)
                            && (expires is null || reference <= expires + parameters.ClockSkew);
                    },
                }).GetAwaiter().GetResult();

            // No claim mapping happens here and none is wanted. JsonWebToken
            // reads the payload as written, so the identity is rebuilt by hand
            // from the two short names this type mints. The mapping is
            // load-bearing in both directions and was measured, not assumed:
            // [Authorize(Roles = ...)] and RequireRole(policy) match on
            // ClaimsIdentity.RoleClaimType, which defaults to ClaimTypes.Role
            // -- the long WS-Federation URI, not "role". A principal carrying
            // the short name alone would pass every signature check and still
            // be denied authorization, which is the exact silent-401 shape
            // this work exists to remove.
            // IsValid is the check, NOT "is SecurityToken null". Measured on both
            // 7.1.2 and 8.14.0, every failure mode below leaves SecurityToken
            // null, so testing that alone happens to give the right answer --
            // but it tests an implementation detail rather than the verdict, and
            // a mutation that made the catch block return true survived all 42
            // tests precisely because nothing here consulted IsValid.
            if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
            {
                return false;
            }

            var payload = jwt.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.First().Value);

            if (!payload.TryGetValue(RoleClaim, out var role)
                || !string.Equals(role, InternalServiceAuthMiddleware.InternalRole, StringComparison.Ordinal))
            {
                // Signature validity is necessary but not sufficient: possession
                // of the signing key alone must not be enough to become
                // InternalService, so a token minted under this key for some
                // other purpose is rejected here even though it verifies.
                return false;
            }

            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, payload.GetValueOrDefault(SubjectPayloadClaim) ?? string.Empty),
                    new Claim(ClaimTypes.Role, role),
                },
                authenticationType: AuthenticationType);
            principal = new ClaimsPrincipal(identity);
            return true;
        }
        catch (Exception)
        {
            // Defensive, and DELIBERATELY not exercised by any test. Measured on
            // both versions the seven services actually resolve (7.1.2 and
            // 8.14.0) and confirmed by mutation: 19 distinct malformed and
            // hostile inputs -- multi-audience arrays, aud as a JSON number, aud
            // as an empty array, iss as an array, non-base64url characters, a
            // null byte, a 100k-character token, an unsigned but well-formed
            // token, a key too short for HMAC-SHA256, and plain garbage -- all
            // come back as IsValid=false with no exception, and deleting this
            // block entirely still leaves the whole suite green. No test can
            // reach it, so this is insurance, not a tested guarantee.
            //
            // It stays because TryValidate's contract above promises it never
            // throws. Without this, a future Microsoft.IdentityModel version
            // that started throwing on some new token shape would turn every
            // rejected fan-out call into a 500 on a public endpoint. The
            // comment that used to sit here named four specific exception types
            // -- ArgumentException, SecurityTokenException, the expired and
            // not-yet-valid pair -- as if each had been observed. None of them
            // was: measurement found the validator returns a verdict instead of
            // throwing in all of those cases, and a list of exception types that
            // reads like a design rationale but is really a guess is worse than
            // saying plainly that the path is unmeasured.
            principal = null;
            return false;
        }
    }
}
