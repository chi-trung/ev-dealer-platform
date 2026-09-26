using Common.Auth;
using Microsoft.Extensions.Configuration;

namespace Common.Auth;

/// <summary>
/// Attaches the internal service credential to every outgoing request from a
/// typed <c>HttpClient</c>, so sibling services can authenticate a
/// machine-to-machine call that carries no user JWT.
///
/// Which credential depends on <see cref="InternalServiceAuth.SendSignedTokenConfigPath"/>:
/// a short-lived signed token when it is on, otherwise the static shared key.
/// Both are sent under the same header so the gateway's
/// <c>StripInternalServiceKeyMiddleware</c> keeps working unchanged.
/// </summary>
/// <remarks>
/// WHY A DELEGATING HANDLER RATHER THAN A LINE IN EACH DATA SERVICE
/// There are four outbound data services (Sales, Vehicle, Customer, User) and
/// thirteen call sites between them. Adding the header at each call site would
/// mean thirteen chances to forget one, and the failure mode is the silent
/// kind this phase exists to remove: a missed header produces a 401 that the
/// caller logs as a warning and treats as "no data", so the report simply
/// comes back empty and the endpoint still says success.
///
/// A handler attached at <c>AddHttpClient</c> registration covers every
/// request that client will ever make, including ones added later.
///
/// WHY THE CREDENTIAL IS SWITCHED AND NOT REPLACED
/// A receiver running pre-token code does not understand a token, and a
/// receiver with <c>RequireSignedToken</c> set refuses the static key. Swapping
/// the sender over before the receivers understand tokens turns the fan-out
/// into a 401 storm that looks exactly like the empty-report bug this work
/// fixed. The receiving side therefore tries the token first and falls back to
/// the key, and both directions are behind their own default-off flag; see
/// <see cref="InternalServiceAuth.SendSignedTokenConfigPath"/> for the only
/// order with no broken window.
///
/// The key is read from <see cref="IConfiguration"/> rather than from
/// <c>Environment.GetEnvironmentVariable</c> so that appsettings.json works
/// too and the value is resolved once per handler instance instead of once
/// per request. Every other config read in this service goes through
/// IConfiguration for the same reason.
/// </remarks>
public sealed class InternalServiceKeyHandler : DelegatingHandler
{
    private readonly string? _key;
    private readonly string? _signingKey;
    private readonly bool _sendSignedToken;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The only constructor. It deliberately does NOT chain to a base
    /// overload that assigns <c>InnerHandler</c>.
    /// </summary>
    /// <remarks>
    /// My first version had a second constructor taking an
    /// <see cref="HttpMessageHandler"/> and passing it to <c>base(innerHandler)</c>
    /// as a test seam. That broke at runtime: the factory builds the pipeline
    /// by taking this handler, setting <c>InnerHandler</c> itself, and appending
    /// the primary handler — and it refuses a handler that already has one:
    ///
    ///   InvalidOperationException: The 'InnerHandler' property must be null.
    ///   'DelegatingHandler' instances provided to 'HttpMessageHandlerBuilder'
    ///   must not be reused or cached.
    ///
    /// The two constructors also made DI ambiguous between them. A handler in
    /// a typed-client pipeline gets its inner handler FROM the factory, so
    /// there is nothing to inject. The tests assert the header by checking the
    /// outgoing request through a stub inner handler they own, which means
    /// constructing the handler the same way the factory does — via this
    /// constructor and then assigning <c>InnerHandler</c> themselves.
    /// </remarks>
    public InternalServiceKeyHandler(IConfiguration configuration)
        : this(configuration, TimeProvider.System)
    {
    }

    /// <summary>
    /// Test seam for the clock only. The token's window has to start at the
    /// moment it is sent, not when the handler was constructed, and the only
    /// way to prove that without waiting out a real five-minute expiry is to
    /// move the clock. <see cref="TimeProvider"/> is the framework's own
    /// abstraction for this (net8.0), so no package is added.
    ///
    /// Public rather than internal on purpose: making it internal would need an
    /// <c>InternalsVisibleTo</c> for the test assembly, which opens every
    /// internal member of this service rather than this one parameter.
    /// DI still binds the single-argument overload above, since that is the one
    /// registered.
    /// </summary>
    public InternalServiceKeyHandler(IConfiguration configuration, TimeProvider timeProvider)
    {
        _key = configuration[InternalServiceAuth.ConfigPath];
        _signingKey = configuration[InternalServiceToken.SigningKeyConfigPath];
        _timeProvider = timeProvider;

        // Read as a string, not through GetValue<bool>: the reporting service
        // references Configuration.Binder but a config read that throws at
        // boot on a malformed value is a worse failure than treating it as
        // off. Unset and "false" behave the same, which is the safe default —
        // the fan-out keeps authenticating with the credential every receiver
        // already understands.
        _sendSignedToken = string.Equals(
            configuration[InternalServiceAuth.SendSignedTokenConfigPath],
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A caller that already set the header explicitly keeps it, so this
        // handler never silently overwrites a deliberate value (which a test or
        // a future forwarder may set). Checked first and covering BOTH
        // credential shapes: once a token is attached, this handler has nothing
        // to add.
        if (!request.Headers.Contains(InternalServiceAuth.HeaderName))
        {
            // Token first, because that is the credential the switch selects.
            // Minting cannot fail once the key is usable — the length is
            // checked on the way in, and an unusable key leaves this branch
            // untaken rather than throwing inside SendAsync, which would turn
            // every fan-out call into a 500 instead of the 401 the target
            // would return anyway.
            if (_sendSignedToken && InternalServiceToken.IsUsableSigningKey(_signingKey))
            {
                request.Headers.TryAddWithoutValidation(
                    InternalServiceAuth.HeaderName,
                    InternalServiceToken.Create(_signingKey!, _timeProvider.GetUtcNow()));
            }
            // Otherwise the static key. A null key here is a developer running
            // ReportingService alone with no siblings to authenticate to: the
            // handler then sends nothing and the target's [Authorize] rejects
            // the call exactly as it did before this change — no worse.
            else if (!string.IsNullOrWhiteSpace(_key))
            {
                request.Headers.TryAddWithoutValidation(InternalServiceAuth.HeaderName, _key);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
