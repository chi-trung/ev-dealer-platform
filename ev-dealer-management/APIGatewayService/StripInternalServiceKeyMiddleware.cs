using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace APIGatewayService;

/// <summary>
/// Strips the service-to-service shared secret from any request arriving over
/// the public gateway, so the gateway cannot be used as a relay for it.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS (Issue #92, found by probing the running stack)
///
/// Five services share one <c>InternalService__Key</c> that ReportingService
/// presents on its data-sync fan-out. The key is a shared secret, and the whole
/// point of a shared secret is that only the parties that already trust each
/// other hold it.
///
/// Ocelot proxies request headers through verbatim, so before this middleware a
/// client on the internet could send the key to the gateway and have it relayed
/// untouched to whichever service the route pointed at. Measured on the running
/// stack, with nothing but the header and no user token:
///
///   GET /api/Orders        no key -> 401   correct key -> 200
///   GET /api/Contracts     no key -> 401   correct key -> 200
///   GET /api/Quotes        no key -> 401   correct key -> 200
///   GET /api/Payments      no key -> 401   correct key -> 200
///   GET /api/customers     no key -> 401   correct key -> 200
///
/// That is the shared secret turned into a public API key: the exact endpoint
/// the fan-out fix was meant to open up to ReportingService, opened up to
/// everyone instead.
///
/// WHY STRIPPING IS SUFFICIENT, NOT OVER-ENGINEERED
///
/// Nothing legitimately needs this header to arrive via the gateway.
/// ReportingService's data services address the services DIRECTLY --
/// <c>Services__SalesService=http://salesservice:80</c> in compose, and the
/// public https service URLs on Render -- not through the gateway. So the
/// header arriving here can only come from a client, and dropping it costs
/// nothing while removing the whole relay path.
///
/// The alternative -- forwarding only for some marker of an internal caller --
/// needs a signal Render does not offer out of the box, and a header a client
/// can also set is not that signal.
///
/// DELIBERATELY NOT A 401
///
/// This removes the header and lets the request proceed. A caller that sent it
/// then behaves exactly as if it had never sent it: the downstream [Authorize]
/// or RequireRole decides, and with no user token and no key that means 401.
/// Rejecting outright would also be defensible, but it turns "a client guessed
/// a header name" into a distinguishable 4xx, and it would break any future
/// legitimate internal path through the gateway before that path exists.
/// </remarks>
public sealed class StripInternalServiceKeyMiddleware
{
    /// <summary>
    /// Matches the constant in Common.Auth.InternalServiceAuth.HeaderName.
    /// Duplicated rather than referenced: APIGatewayService has no ProjectReference
    /// to Common, and adding one purely to share a header-name string would make
    /// the gateway depend on the assembly holding the fan-out code. If the header
    /// is ever renamed, both sides must move together -- the test
    /// GatewayHeaderStrippingTests asserts the literal against this middleware,
    /// and InternalServiceAuthTests pins the constant on the other side.
    /// </summary>
    public const string InternalKeyHeader = "X-Internal-Key";

    private readonly RequestDelegate _next;

    public StripInternalServiceKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Remove unconditionally rather than only when the value looks wrong:
        // the gateway has no business carrying this header in either direction,
        // and a conditional check would leave a valid key sitting in memory on
        // the request for downstream middleware to misread.
        context.Request.Headers.Remove(InternalKeyHeader);

        return _next(context);
    }
}

public static class StripInternalServiceKeyExtensions
{
    /// <summary>
    /// Must be registered BEFORE <c>UseOcelot()</c>. Ocelot's responder
    /// short-circuits the pipeline, so middleware added after it never runs and
    /// the header would reach the downstream untouched.
    /// </summary>
    public static IApplicationBuilder UseStripInternalServiceKey(this IApplicationBuilder app)
        => app.UseMiddleware<StripInternalServiceKeyMiddleware>();
}
