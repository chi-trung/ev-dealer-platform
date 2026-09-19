using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Common.Health;

/// <summary>
/// Health check registration helpers for the services. See issue #135: every
/// service called <c>AddHealthChecks()</c> with zero registered checks, so
/// <c>/health</c> returned 200 unconditionally — including for services whose
/// broker consumer had permanently died at boot and whose DB was unreachable.
/// </summary>
public static class BrokerHealthCheckExtensions
{
    // The check tag that separates "can this instance route traffic" from
    // "is the process alive". Render's healthCheckPath and the gateway's
    // aggregate both treat 200 as healthy, so a check that only proves the
    // process is up is not a readiness signal (#134).
    public const string ReadinessTag = "readiness";

    /// <summary>
    /// Registers a broker connectivity check that opens its OWN short-lived
    /// connection to the configured broker, independent of any
    /// publisher/consumer singleton. <paramref name="name"/> is the check name
    /// as it appears in the health response.
    /// </summary>
    /// <remarks>
    /// Why a self-contained probe rather than asking the registered publisher
    /// whether its <c>IConnection</c> is open: every broker client here is an
    /// <c>AddSingleton</c> that .NET constructs on FIRST resolution, and
    /// nothing in startup resolves one — controllers take it as a constructor
    /// parameter and no request has arrived yet (#134 traced this same lazy
    /// lifetime for SalesService's publisher). Asking the container for a
    /// reporter therefore legitimately returns null at boot even when the
    /// broker is perfectly healthy, and reporting that as Unhealthy is a false
    /// negative that fails the first deploy gate (#133's exact failure mode).
    /// The <c>AddHostedService</c> consumers are worse still: that method
    /// registers the type only as <c>IHostedService</c>, so a hosted consumer
    /// is not resolvable by its own type at all. Probing the endpoint directly
    /// answers the question the health check actually asks — can this process
    /// reach the broker? — without depending on singleton construction timing.
    /// </remarks>
    public static IHealthChecksBuilder AddBrokerProbeCheck(
        this IHealthChecksBuilder builder,
        string name = "rabbitmq")
    {
        return builder.AddCheck<BrokerProbeHealthCheck>(name, null, new[] { ReadinessTag });
    }

    /// <summary>
    /// Registers a database connectivity check for the service's own
    /// <typeparamref name="TContext"/>. <paramref name="name"/> is the check
    /// name as it appears in the health response.
    /// </summary>
    /// <typeparam name="TContext">The service's DbContext.</typeparam>
    public static IHealthChecksBuilder AddDatabaseCheck<TContext>(
        this IHealthChecksBuilder builder,
        string name = "database")
        where TContext : Microsoft.EntityFrameworkCore.DbContext
    {
        return builder.AddCheck<DatabaseHealthCheck<TContext>>(name, null, new[] { ReadinessTag });
    }

    /// <summary>
    /// Writes the health report as JSON with per-check detail, for the detail
    /// endpoint (<c>/health</c> here). The framework default writes the bare
    /// word "Unhealthy", which cannot tell a dead broker from a dead database
    /// when the gateway aggregates it.
    /// </summary>
    public static Task WriteHealthReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        // Unhealthy/ Degraded are still JSON-serializable; the status code is
        // set by MapHealthChecks from this report, so 503 arrives automatically.
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds,
                    // Data may be null; HealthCheckResult tags land here.
                    data = e.Value.Data
                })
        });
        return context.Response.WriteAsync(payload);
    }

    // The checks below resolve their dependencies through the constructor — DI
    // hands them the app's real root container, so they observe the same
    // singletons the request pipeline uses. This is why they are classes rather
    // than the lambda AddCheck overload: that overload resolves the check
    // instance from the health service's own scope, and a lambda cannot take
    // constructor injection. Building a second root container instead (a first
    // attempt here) constructs NEW singletons on every health request and
    // reports state the running service never uses — #135's live probe caught
    // exactly that.

    private sealed class BrokerProbeHealthCheck : IHealthCheck
    {
        // RabbitMQ.Client's default is 30s, which would hold a deploy gate or
        // crash-loop probe hostage for half a minute per check on an
        // unreachable broker. 3s is the same band the gateway's upstream
        // timeouts run in, and a broker that answers in more than 3s is not a
        // broker this service can use.
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

        private readonly IConfiguration _configuration;
        public BrokerProbeHealthCheck(IConfiguration configuration) => _configuration = configuration;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Same keys every broker client in this repo reads, so a service's
            // /health probes the broker it actually publishes to and consumes
            // from — no second source of truth to drift out of sync with.
            var hostName = _configuration["RabbitMQ:HostName"] ?? "localhost";
            var port = int.TryParse(_configuration["RabbitMQ:Port"], out var p) ? p : 5672;

            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = hostName,
                    Port = port,
                    UserName = _configuration["RabbitMQ:UserName"] ?? "guest",
                    Password = _configuration["RabbitMQ:Password"] ?? "guest",
                    RequestedConnectionTimeout = ProbeTimeout,
                    // One-shot probe: no recovery loop, no background threads
                    // to clean up — the connection is closed by the using.
                    AutomaticRecoveryEnabled = false,
                    // Names the connection in the broker's management UI so
                    // these probes are distinguishable from real clients.
                    ClientProvidedName = "health-check"
                };

                // RabbitMQ.Client's IConnection/IModel predate IAsyncDisposable,
                // so the probe uses a synchronous using; both Close
                // gracefully, and the channel is disposed before the
                // connection so the broker logs no unexpected client close.
                //
                // The exception path cannot leak a connection even though this
                // `using` sits inside the try: CreateConnection throws from
                // inside the AMQP handshake (a non-broker listener that
                // accepts the TCP socket still fails "connection.start was
                // never received"), so the variable is never assigned on the
                // path the catch handles. A handle is only ever handed back
                // when a real broker answered — the case where this using
                // disposes it in order. Verified by trying to construct the
                // leak, which is not possible against a non-broker.
                using var connection = factory.CreateConnection("health-check");
                using var channel = connection.CreateModel();
                return await Task.FromResult(HealthCheckResult.Healthy(
                    $"RabbitMQ is reachable at {hostName}:{port}",
                    new Dictionary<string, object> { ["host"] = hostName, ["port"] = port }));
            }
            catch (Exception ex)
            {
                // BrokerUnreachableException is the expected case (broker down,
                // wrong host, loopback-restricted `guest` from a remote host —
                // see docs/RENDER.md). Anything else is still "not healthy":
                // reporting Unhealthy is the point of #135, and the exception
                // data is what makes the 503 actionable in the JSON body.
                return HealthCheckResult.Unhealthy(
                    $"RabbitMQ is unreachable at {hostName}:{port}", ex,
                    new Dictionary<string, object> { ["host"] = hostName, ["port"] = port });
            }
        }
    }

    private sealed class DatabaseHealthCheck<TContext> : IHealthCheck
        where TContext : Microsoft.EntityFrameworkCore.DbContext
    {
        private readonly IServiceProvider _services;
        public DatabaseHealthCheck(IServiceProvider services) => _services = services;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            await using var scope = _services.CreateAsyncScope();
            // GetService, not GetRequiredService: a misconfigured provider
            // (#90's hard-fail) means the context may be unconstructible, and
            // the health check should report that rather than throw out of
            // the check itself.
            var dbContext = scope.ServiceProvider.GetService<TContext>();
            if (dbContext is null)
            {
                return HealthCheckResult.Unhealthy(
                    $"{typeof(TContext).Name} is not resolvable; the provider may be misconfigured");
            }
            try
            {
                // CanConnect forces a real round-trip, which is what every
                // endpoint will do anyway; it works identically on both
                // providers this helper supports.
                //
                // Note the reachability asymmetry: every service here calls
                // Migrate()/EnsureCreated BEFORE app.Run(), and #90 made a bad
                // connection string rethrow out of startup, so an unreachable DB
                // at boot kills the process and this check's Unhealthy arm is
                // reached only when the DB drops AFTER startup (which is what
                // a rolling deploy gate needs to catch — an instance that was
                // healthy when it started answering traffic). The broker check
                // below has no such guard: a broker failure at boot leaves the
                // process alive and listening, which is exactly why /health
                // must not answer 200 for it.
                return await dbContext.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("Database is reachable")
                    : HealthCheckResult.Unhealthy("Database is unreachable");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "Database probe threw; the provider may be misconfigured", ex);
            }
        }
    }
}
