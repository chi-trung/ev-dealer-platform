using System.Net;
using Common.Auth;
using ev_dealer_reporting.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// The sending half of issue #92: what <c>InternalServiceKeyHandler</c> actually
/// puts on the wire, under each stage of the rollout.
/// </summary>
/// <remarks>
/// WHY THIS FILE EXISTS
/// The handler had no tests at all before signed tokens were added, so the
/// whole outbound path was unverified — a change there that stopped sending a
/// header would show up as an empty report and nothing else, which is the
/// exact failure mode the middleware work exists to remove.
///
/// WHAT MUST BE TRUE
/// 1. Default (nothing configured) — unchanged from before this work: the
///    static key goes out when one is configured, and nothing goes out when it
///    is not, so a developer running ReportingService alone is no worse off.
/// 2. <c>SendSignedToken</c> on — a token goes out, and it is one the
///    receiving middleware accepts. Asserted by round-tripping it through
///    <see cref="InternalServiceToken.TryValidate"/>, not by checking it merely
///    looks like a JWT: a token with the wrong audience or a missing role would
///    satisfy every string assertion here and be rejected by the receiver.
/// 3. The switch is reversible and the two states are distinguishable — a
///    receiver staged in between accepts both, so nothing here can pick the
///    wrong credential for it.
/// 4. An explicitly-set header is never overwritten. Pinned because the token
///    branch and the key branch are now separate, and either could clobber a
///    caller's deliberate value.
/// </remarks>
public class InternalServiceKeyHandlerTests
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long-ok";
    private const string StaticKey = "the-old-static-key";

    /// <summary>
    /// Captures the request the handler built and answers it, so the assertions
    /// are about what actually went out. Assigning <c>InnerHandler</c> here is
    /// how the HttpClientFactory does it too, which is the only way to
    /// construct one of these outside DI.
    /// </summary>
    private static async Task<HttpRequestMessage> SendAsync(
        IConfiguration configuration, string? presetHeader = null)
    {
        var stub = new StubInnerHandler();
        var handler = new InternalServiceKeyHandler(configuration)
        {
            InnerHandler = stub,
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://sibling/api/orders");
        if (presetHeader is not null)
        {
            request.Headers.TryAddWithoutValidation(InternalServiceAuth.HeaderName, presetHeader);
        }

        var captured = stub.Capture();
        await client.SendAsync(request);
        return await captured.Task;
    }

    private static IConfiguration Config(
        string? staticKey = StaticKey,
        string? signingKey = null,
        bool sendToken = false) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [InternalServiceAuth.ConfigPath] = staticKey,
                [InternalServiceToken.SigningKeyConfigPath] = signingKey,
                [InternalServiceAuth.SendSignedTokenConfigPath] = sendToken ? "true" : null,
            })
            .Build();

    // ---------- the unchanged default ----------

    [Fact]
    public async Task WithNothingConfigured_TheStaticKeyIsSent()
    {
        // The pre-existing behaviour, pinned so the token work cannot quietly
        // change what a deploy that has set no new variables sends.
        var sent = await SendAsync(Config());

        Assert.Equal(StaticKey, HeaderValue(sent));
    }

    [Fact]
    public async Task WithNoKeysAtAll_NothingIsSent()
    {
        // A developer running ReportingService alone has no sibling to
        // authenticate to. Sending nothing leaves the target's [Authorize] to
        // reject the call exactly as it did before any of this work.
        var sent = await SendAsync(Config(staticKey: null, signingKey: null));

        Assert.False(sent.Headers.Contains(InternalServiceAuth.HeaderName));
    }

    // ---------- the token path ----------

    [Fact]
    public async Task WithSendSignedToken_On_AValidServiceTokenIsSent()
    {
        var sent = await SendAsync(Config(signingKey: SigningKey, sendToken: true));

        var presented = HeaderValue(sent);
        Assert.NotNull(presented);
        // Round-trip rather than string-match: this asserts the receiving
        // middleware would actually accept it, which a "looks like a JWT"
        // check does not.
        Assert.True(InternalServiceToken.TryValidate(
            presented, SigningKey, DateTimeOffset.UtcNow, out var principal));
        Assert.True(principal!.IsInRole(InternalServiceAuthMiddleware.InternalRole));
    }

    [Fact]
    public async Task TheSentToken_IsNotTheStaticKey()
    {
        // If the token branch silently fell through to the key branch, the
        // switch would appear to work while the credential on the wire never
        // changed — and the rollout would be believed complete when it is not.
        var sent = await SendAsync(Config(signingKey: SigningKey, sendToken: true));

        Assert.NotEqual(StaticKey, HeaderValue(sent));
    }

    [Fact]
    public async Task WithSendSignedToken_On_AndNoUsableSigningKey_TheStaticKeyIsSent()
    {
        // Half-configured: the flag is on but the key never got filled in. A
        // token cannot be minted, and sending nothing would 401 the whole
        // fan-out, so it falls back to the credential the receivers still
        // accept. The flag being on is not evidence the key is present.
        var sent = await SendAsync(Config(signingKey: "too-short", sendToken: true));

        Assert.Equal(StaticKey, HeaderValue(sent));
    }

    [Fact]
    public async Task WithSendSignedToken_On_AndNoStaticKeyEither_NothingIsSent()
    {
        // Both branches unusable: nothing is invented, and nothing throws
        // inside the handler, which would turn a 401 into a 500.
        var sent = await SendAsync(
            Config(staticKey: null, signingKey: null, sendToken: true));

        Assert.False(sent.Headers.Contains(InternalServiceAuth.HeaderName));
    }

    [Fact]
    public async Task EachRequestGetsAFreshlyMintedToken()
    {
        // The whole point of the change is a bounded window. A handler that
        // minted once at construction would send the same token for the life of
        // the process — five minutes of validity measured from whenever the
        // service started, which for a long-lived service is no window at all,
        // and it would keep being replayed after it expired.
        //
        // A cache cannot be caught by sending twice quickly, because two mints
        // a millisecond apart produce the same string. So the clock is moved
        // past the first token's whole validity between the two requests: a
        // cached token then arrives expired, a per-request one does not.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var configuration = Config(signingKey: SigningKey, sendToken: true);
        var stub = new StubInnerHandler();
        var handler = new InternalServiceKeyHandler(configuration, clock)
        {
            InnerHandler = stub,
        };
        using var client = new HttpClient(handler);

        var first = await SendCapturingAsync(client, stub);
        clock.Advance(TimeSpan.FromSeconds(
            InternalServiceToken.LifetimeSeconds + InternalServiceToken.ClockSkewSeconds + 1));
        var second = await SendCapturingAsync(client, stub);

        Assert.NotEqual(first, second);
        Assert.True(InternalServiceToken.TryValidate(
            second, SigningKey, clock.GetUtcNow(), out var principal));
        Assert.True(principal!.IsInRole(InternalServiceAuthMiddleware.InternalRole));
        // The first token really is dead by the advanced clock, so a handler
        // still sending it fails above rather than merely looking different.
        Assert.False(InternalServiceToken.TryValidate(
            first, SigningKey, clock.GetUtcNow(), out _));
    }

    // ---------- the caller's own header ----------

    [Fact]
    public async Task AnExplicitlySetHeader_IsNeverOverwritten()
    {
        // The token branch and the key branch are now separate paths, either of
        // which could clobber a caller's deliberate value. Pinned for both
        // states, not just the default.
        foreach (var sendToken in new[] { false, true })
        {
            var sent = await SendAsync(
                Config(signingKey: SigningKey, sendToken: sendToken),
                presetHeader: "caller-chose-this");

            Assert.Equal("caller-chose-this", HeaderValue(sent));
        }
    }

    /// <summary>
    /// Sends one request through the handler and returns the header value that
    /// went out. A fresh capture sink each time — a
    /// <see cref="TaskCompletionSource{TResult}"/> is set once, so sharing one
    /// across two requests would hand back the first both times and turn this
    /// into an equality assertion.
    /// </summary>
    private static async Task<string> SendCapturingAsync(
        HttpClient client, StubInnerHandler stub)
    {
        var captured = stub.Capture();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://sibling/api/orders");
        await client.SendAsync(request);
        return (await captured.Task).Headers
            .GetValues(InternalServiceAuth.HeaderName).First();
    }

    private static string? HeaderValue(HttpRequestMessage request) =>
        request.Headers.TryGetValues(InternalServiceAuth.HeaderName, out var values)
            ? values.First()
            : null;

    private sealed class StubInnerHandler : HttpMessageHandler
    {
        private TaskCompletionSource<HttpRequestMessage>? _captured;

        public TaskCompletionSource<HttpRequestMessage> Capture()
        {
            _captured = new TaskCompletionSource<HttpRequestMessage>();
            return _captured;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Captured, not returned: the assertions inspect the request that
            // was actually built, after every handler in the pipeline ran.
            _captured?.TrySetResult(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// A clock the test moves by hand. The token window is five minutes, and
    /// proving the handler mints per request means waiting out a real expiry
    /// would add six minutes to every run of this suite.
    /// </summary>
    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
