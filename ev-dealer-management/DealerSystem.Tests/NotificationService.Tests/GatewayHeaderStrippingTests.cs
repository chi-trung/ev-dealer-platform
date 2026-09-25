using APIGatewayService;
using Common.Auth;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #92: pins that the gateway removes the service-to-service shared
/// secret before Ocelot can relay it.
/// </summary>
/// <remarks>
/// The leak this exists to prevent was measured on the running stack before the
/// middleware existed: Ocelot proxies request headers verbatim, so a client
/// sending <c>X-Internal-Key</c> to the public gateway had it forwarded
/// untouched, and the downstream services promoted the caller to an
/// <c>InternalService</c> principal. With no user token at all:
///
///   GET /api/Orders    no key -&gt; 401    correct key -&gt; 200
///
/// That made the shared secret a public API key. These tests assert the
/// stripping, and the cross-assembly test asserts the header name still agrees
/// with the one Common.Auth uses, since the two sides share only the literal.
/// </remarks>
public class GatewayHeaderStrippingTests
{
    private const string TheKey = "dev-only-internal-key-not-for-production";

    private static async Task<HttpContext> PassThroughGatewayAsync(
        string headerName, string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers[headerName] = headerValue;
        }

        var reachedDownstream = false;
        var middleware = new StripInternalServiceKeyMiddleware(
            _ => { reachedDownstream = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(reachedDownstream,
            "the middleware must call next(); it removes a header, it does not block the request");
        return context;
    }

    [Fact]
    public async Task TheInternalKeyIsRemoved_BeforeOcelotSeesTheRequest()
    {
        var context = await PassThroughGatewayAsync(
            StripInternalServiceKeyMiddleware.InternalKeyHeader, TheKey);

        Assert.False(context.Request.Headers.ContainsKey(
            StripInternalServiceKeyMiddleware.InternalKeyHeader));
    }

    [Fact]
    public async Task AValidKeyIsRemoved_NotJustAMalformedOne()
    {
        // A conditional check ("remove only if the value looks wrong") would
        // pass a test that used a garbage value and leave the real key in
        // flight. This is the value that actually authenticated against the
        // running services.
        //
        // The sweep is over EVERY header value rather than just the named one:
        // a rename on either side, or a middleware that copied the value under
        // a second key, would put the key back into the request and only this
        // form notices.
        var context = await PassThroughGatewayAsync(
            StripInternalServiceKeyMiddleware.InternalKeyHeader, TheKey);

        Assert.DoesNotContain(
            context.Request.Headers.SelectMany(h => h.Value.ToArray()),
            value => value is not null && value.Contains(TheKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARequestWithNoInternalKeyIsUnaffected()
    {
        var context = await PassThroughGatewayAsync(
            StripInternalServiceKeyMiddleware.InternalKeyHeader, headerValue: null);

        Assert.Empty(context.Request.Headers
            .Where(h => h.Key.Equals(StripInternalServiceKeyMiddleware.InternalKeyHeader,
                                     StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task UnrelatedHeadersArePassedThrough()
    {
        // The gateway must not become a general header-stripping proxy: the
        // Authorization header has to survive, or every authenticated frontend
        // request would start failing.
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer some-user-token";
        context.Request.Headers["Content-Type"] = "application/json";
        context.Request.Headers[StripInternalServiceKeyMiddleware.InternalKeyHeader] = TheKey;

        var middleware = new StripInternalServiceKeyMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal("Bearer some-user-token", context.Request.Headers["Authorization"]);
        Assert.Equal("application/json", context.Request.Headers["Content-Type"]);
        Assert.False(context.Request.Headers.ContainsKey(
            StripInternalServiceKeyMiddleware.InternalKeyHeader));
    }

    [Fact]
    public void TheGatewayHeaderNameMatchesTheOneTheServicesRead()
    {
        // APIGatewayService has no ProjectReference to Common -- sharing one
        // header-name string would make the gateway depend on the assembly
        // holding the fan-out code. The cost of that choice is that the two
        // sides can drift, so the agreement is asserted here instead of
        // assumed. If Common.Auth.InternalServiceAuth.HeaderName is ever
        // renamed, this test fails rather than leaving a silent relay open.
        Assert.Equal(InternalServiceAuth.HeaderName,
                     StripInternalServiceKeyMiddleware.InternalKeyHeader);
    }
}
