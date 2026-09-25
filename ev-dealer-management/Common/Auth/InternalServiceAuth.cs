using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Common.Auth;

/// <summary>
/// Shared header name and constant-time comparison for the internal
/// service-to-service key. See <see cref="InternalServiceAuthMiddleware"/> for
/// why this exists.
/// </summary>
public static class InternalServiceAuth
{
    /// <summary>
    /// The single header an internal caller sets. A distinct name (rather than
    /// reusing <c>Authorization</c>) keeps the two credential paths visibly
    /// separate in access logs and makes it obvious which one a request used.
    /// </summary>
    public const string HeaderName = "X-Internal-Key";

    /// <summary>
    /// Config path for the shared secret. Double underscore in the environment
    /// form (<c>InternalService__Key</c>), same convention as
    /// <c>ConnectionStrings__DefaultConnection</c> and <c>Jwt__Key</c>.
    /// </summary>
    public const string ConfigPath = "InternalService:Key";

    /// <summary>
    /// Compares two secrets without leaking their contents through timing.
    ///
    /// <see cref="string.Equals(string?, string?)"/> returns as soon as it
    /// finds a differing byte, so a caller who can measure response time can
    /// recover the expected value one character at a time. That is not a
    /// theoretical concern for a credential that is sent on every internal
    /// request, so the comparison is over fixed-length buffers and stops
    /// nowhere early.
    ///
    /// Length is compared FIRST and returned as a result rather than an early
    /// throw, because a differing length is itself the answer: no amount of
    /// further comparison can make unequal-length secrets equal, and padding
    /// them to compare would waste a comparison on a case that is already
    /// decided.
    /// </summary>
    public static bool FixedTimeEquals(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
            return false;

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(provided);
        if (a.Length != b.Length)
            return false;

        // CryptographicOperations.FixedTimeEquals is the runtime's own
        // constant-time primitive. It needs equal-length inputs (checked
        // above) and throws otherwise, so the length guard is load-bearing.
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>
/// Lets one service call another on the internal network WITHOUT a user JWT,
/// by presenting a shared secret instead.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS
/// <c>POST /api/reports/synchronize-data</c> fans out over HTTP to Sales,
/// Vehicle and Customer services. Those controllers carry <c>[Authorize]</c>
/// (added in #137), so every call returned 401 and the service logged
/// "Unauthorized", returned <c>{"success":true}</c>, and wrote zero rows. The
/// endpoint reported success while doing nothing.
///
/// WHY A SHARED KEY RATHER THAN FORWARDING THE CALLER'S JWT
/// Forwarding the incoming token would tie an internal fan-out to a human
/// session. Any report refresh that runs on a timer or on a queue consumer
/// has no user token to forward, and would keep failing after this change —
/// the same silent-empty-report failure under a new name. A service identity
/// is the correct shape for machine-to-machine traffic: it exists whenever the
/// caller is up, and it is scoped to the machine rather than to a person.
///
/// The two approaches are not exclusive. A forwarder can be added later
/// alongside this; this one has to come first, because it is the only one that
/// works for an unattended refresh.
///
/// WHAT THIS DOES NOT DO
/// It does not decide WHICH endpoints are open to internal callers — that is
/// still the <c>[Authorize]</c> attribute's job. This middleware runs BEFORE
/// authorization and promotes a request carrying the correct key to
/// <see cref="ClaimsPrincipal"/> with the internal role, so the normal
/// authorization rules then decide. An endpoint that keeps <c>[Authorize]</c>
/// therefore still rejects a request with no key AND no user token — the
/// middlewares do not open anything by themselves.
/// </remarks>
public sealed class InternalServiceAuthMiddleware
{
    // The role the promoted principal carries. Naming it distinctly keeps
    // these requests identifiable in authorization policies and logs.
    public const string InternalRole = "InternalService";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalServiceAuthMiddleware> _logger;

    public InternalServiceAuthMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<InternalServiceAuthMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var expected = _configuration[InternalServiceAuth.ConfigPath];

        // A service that has no key configured cannot verify anyone, so it must
        // not promote anyone either. Returning here (rather than 401-ing) is
        // deliberate: this middleware's job is to ADD an internal identity, not
        // to become the thing that rejects anonymous traffic — the [Authorize]
        // attributes already do that, and duplicating it here would mean two
        // places to update every time an endpoint is opened.
        if (string.IsNullOrWhiteSpace(expected))
        {
            _logger.LogWarning(
                "{ConfigPath} is not set on {Service}; internal service-to-service calls will not be authenticated",
                InternalServiceAuth.ConfigPath,
                "this service");
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(InternalServiceAuth.HeaderName, out var supplied)
            || supplied.Count == 0)
        {
            await _next(context);
            return;
        }

        // Only the first value is considered: a request carrying two copies of
        // the header is ambiguous, and picking one of them would let a proxy
        // append a value that a downstream service reads differently.
        var provided = supplied[0];
        if (!InternalServiceAuth.FixedTimeEquals(expected, provided))
        {
            // Logged as a warning without the value. A wrong key here is
            // either a misconfigured deployment or someone probing the
            // internal surface — both worth seeing, neither is a crash.
            _logger.LogWarning(
                "Rejected internal call to {Path}: {Header} did not match the configured key",
                context.Request.Path.Value,
                InternalServiceAuth.HeaderName);
            await _next(context);
            return;
        }

        // Promote the identity. The principal is REPLACED rather than extended
        // so an internal call cannot inherit a stale ambient user from a
        // previous request on a pooled connection.
        var identity = new System.Security.Claims.ClaimsIdentity(
            new[]
            {
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Name, "internal-service"),
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Role, InternalRole),
            },
            authenticationType: "InternalServiceKey");

        context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        _logger.LogDebug(
            "Accepted internal call to {Path} using the configured service key",
            context.Request.Path.Value);

        await _next(context);
    }
}

public static class InternalServiceAuthExtensions
{
    /// <summary>
    /// Registers the middleware. MUST be called between
    /// <c>app.UseAuthentication()</c> and <c>app.UseAuthorization()</c> so a
    /// promoted identity is in place before authorization evaluates it.
    /// </summary>
    public static IApplicationBuilder UseInternalServiceAuth(this IApplicationBuilder app)
    {
        return app.UseMiddleware<InternalServiceAuthMiddleware>();
    }
}
