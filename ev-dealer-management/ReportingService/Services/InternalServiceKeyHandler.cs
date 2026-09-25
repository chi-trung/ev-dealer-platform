using Common.Auth;
using Microsoft.Extensions.Configuration;

namespace ev_dealer_reporting.Services;

/// <summary>
/// Attaches the shared internal service key to every outgoing request from a
/// typed <c>HttpClient</c>, so sibling services can authenticate a
/// machine-to-machine call that carries no user JWT.
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
/// The key is read from <see cref="IConfiguration"/> rather than from
/// <c>Environment.GetEnvironmentVariable</c> so that appsettings.json works
/// too and the value is resolved once per handler instance instead of once
/// per request. Every other config read in this service goes through
/// IConfiguration for the same reason.
/// </remarks>
public sealed class InternalServiceKeyHandler : DelegatingHandler
{
    private readonly string? _key;

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
    {
        _key = configuration[InternalServiceAuth.ConfigPath];
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Null key: a developer running ReportingService alone, with no
        // siblings to authenticate to, has none configured. The handler then
        // sends nothing and the target's [Authorize] rejects the call exactly
        // as it did before this change — no worse.
        //
        // Contains check: a caller that already set the header explicitly
        // keeps it, so this handler never silently overwrites a deliberate
        // value (which a test or a future forwarder may set).
        if (!string.IsNullOrWhiteSpace(_key)
            && !request.Headers.Contains(InternalServiceAuth.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(InternalServiceAuth.HeaderName, _key);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
