using System.Security.Claims;
using Common.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #92 (P2): pins the behaviour of the internal service key that
/// ReportingService's data-sync fan-out depends on.
/// </summary>
/// <remarks>
/// WHY THIS NEEDS PINNING
/// Before this existed, every fan-out call returned 401. The failure was
/// silent from the caller's point of view: each data service logged a warning,
/// returned an empty list, and the sync endpoint still answered
/// <c>{"success":true}</c> while writing nothing. There is no other test in
/// the suite that exercises the middleware, and the integration tests use
/// in-process fakes rather than this HTTP boundary — so a regression here
/// would pass the whole suite again.
///
/// THE THREE THINGS THAT MATTER
/// 1. A correct key promotes the principal, so <c>[Authorize]</c> stops
///    rejecting a machine call.
/// 2. A wrong or missing key leaves the principal untouched, so the request
///    still reaches authorization and is rejected. A middleware that fell
///    through to "authenticated" on failure would open every <c>[Authorize]</c>
///    endpoint in the three services.
/// 3. The comparison does not leak the secret through timing.
/// </remarks>
public class InternalServiceAuthTests
{
    private const string ConfiguredKey = "test-internal-key-value";

    private static IConfiguration Config(string? key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [InternalServiceAuth.ConfigPath] = key,
            })
            .Build();

    private static async Task<HttpContext> RunAsync(
        IConfiguration configuration, string? providedKey)
    {
        var context = new DefaultHttpContext();
        if (providedKey is not null)
        {
            context.Request.Headers[InternalServiceAuth.HeaderName] = providedKey;
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

    [Fact]
    public async Task CorrectKey_PromotesPrincipal_SoAuthorizeStopsRejecting()
    {
        var context = await RunAsync(Config(ConfiguredKey), ConfiguredKey);

        Assert.Equal("InternalServiceKey", context.User.Identity?.AuthenticationType);
        Assert.Contains(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);
    }

    [Fact]
    public async Task WrongKey_LeavesPrincipalAnonymous_SoAuthorizeStillRejects()
    {
        var context = await RunAsync(Config(ConfiguredKey), "not-the-key");

        // Asserted on the ABSENCE of the InternalService role, not on
        // !IsAuthenticated: DefaultHttpContext starts with a null identity, so
        // Assert.False(null) passes no matter what the middleware did. If a bad
        // key ever promoted, every [Authorize] endpoint in SalesService,
        // VehicleService and CustomerService would be open.
        Assert.DoesNotContain(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);
    }

    [Fact]
    public async Task MissingKeyHeader_LeavesPrincipalAnonymous()
    {
        var context = await RunAsync(Config(ConfiguredKey), providedKey: null);

        Assert.DoesNotContain(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);
    }

    [Fact]
    public async Task UnconfiguredService_PromotesNobody_EvenWithAKey()
    {
        // A service that has no key cannot verify anyone, so it must not accept
        // anyone. This is the case that protects a partially-configured
        // deployment from being open by accident.
        //
        // Asserted as "no InternalService role claim" rather than
        // !IsAuthenticated: DefaultHttpContext leaves User.Identity null, so
        // Assert.False(null) passes whatever the middleware did. Claim 2 says
        // DefaultHttpContext's User is a ClaimsPrincipal with NO claims, so the
        // absence of the role is the assertion that actually has teeth.
        var context = await RunAsync(Config(key: null), "anything-at-all");

        Assert.DoesNotContain(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);
    }

    [Fact]
    public async Task EmptyExpectedKey_PromotesNobody()
    {
        // A blank key is not a valid shared secret. Treating "" as a value
        // would mean any request sending an empty header authenticates. Same
        // claim-based assertion as above, for the same reason.
        var context = await RunAsync(Config(string.Empty), string.Empty);

        Assert.DoesNotContain(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);
    }

    [Theory]
    // BOTH directions, because a length check can be wrong in either direction
    // and each half hides the other. Mutation testing caught exactly this: a
    // guard written as `a.Length < b.Length` (reject only over-long keys) left
    // every shorter-key case green.
    [InlineData(-1)]   // one character SHORTER than the real key
    [InlineData(1)]    // one character LONGER than the real key
    public async Task KeyOfDifferentLength_IsRejected(int trim)
    {
        var provided = trim < 0 ? ConfiguredKey[..^1] : ConfiguredKey + "x";

        var context = await RunAsync(Config(ConfiguredKey), provided);

        Assert.DoesNotContain(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);
    }

    [Theory]
    // Same length, differing only in the last character — the case a
    // non-constant-time comparison is most likely to leak.
    [InlineData("test-internal-key-valua")]
    [InlineData("Test-internal-key-value")]
    [InlineData("test-internal-key-valuE")]
    public async Task KeyDifferingByOneCharacter_IsRejected(string provided)
    {
        var context = await RunAsync(Config(ConfiguredKey), provided);

        Assert.DoesNotContain(context.User.Claims,
            c => c.Type == ClaimTypes.Role && c.Value == InternalServiceAuthMiddleware.InternalRole);
    }

    [Fact]
    public void FixedTimeEquals_RejectsNullAndEmpty()
    {
        Assert.False(InternalServiceAuth.FixedTimeEquals(null, "anything"));
        Assert.False(InternalServiceAuth.FixedTimeEquals("anything", null));
        Assert.False(InternalServiceAuth.FixedTimeEquals(null, null));
        Assert.False(InternalServiceAuth.FixedTimeEquals(string.Empty, string.Empty));
        Assert.False(InternalServiceAuth.FixedTimeEquals("key", string.Empty));
    }

    [Fact]
    public void FixedTimeEquals_AcceptsOnlyTheExactValue()
    {
        Assert.True(InternalServiceAuth.FixedTimeEquals("abc", "abc"));
        Assert.False(InternalServiceAuth.FixedTimeEquals("abc", "abd"));
        Assert.False(InternalServiceAuth.FixedTimeEquals("abc", "abcd"));
    }

    [Fact]
    public async Task PromotedIdentity_ReplacesAnyAmbientUser()
    {
        // A pooled connection can carry a previous request's identity. The
        // principal is REPLACED, not extended, so an internal call cannot
        // inherit a stale user — including one with real user privileges.
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "someone-else") },
            authenticationType: "JwtBearer"));

        var middleware = new InternalServiceAuthMiddleware(
            _ => Task.CompletedTask,
            Config(ConfiguredKey),
            NullLogger<InternalServiceAuthMiddleware>.Instance);
        context.Request.Headers[InternalServiceAuth.HeaderName] = ConfiguredKey;

        await middleware.InvokeAsync(context);

        Assert.DoesNotContain(context.User.Claims,
            c => c.Type == ClaimTypes.Name && c.Value == "someone-else");
        Assert.Contains(context.User.Claims,
            c => c.Type == ClaimTypes.Name && c.Value == "internal-service");
    }
}
