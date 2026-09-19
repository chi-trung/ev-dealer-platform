using Serilog;
using Microsoft.AspNetCore.Builder;
using Newtonsoft.Json.Linq;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

// Issue #63: Serilog bootstrap — the convention NotificationService has
// run since well before this repo's CI era: sinks configured from
// appsettings.json, console startup failures also land as Fatal in the
// daily-rolling file (mounted at /app/Logs), CloseAndFlush on exit.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build())
    .CreateLogger();

try
{
    Log.Information("Starting APIGatewayService...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog(); // Issue #63: route all ILogger<T> through the static Serilog logger above
    
    // Downstream endpoint rewrites, as a LIST of {From,To} rather than a map:
    // .NET config keys can't contain ':' (the path separator), so "localhost:7001"
    // only works as a leaf value. Env form: Gateway__Rewrites__0__From=...
    var rewrites = (builder.Configuration
        .GetSection("Gateway:Rewrites")
        .Get<List<GatewayRewrite>>() ?? new List<GatewayRewrite>())
        .Where(r => !string.IsNullOrWhiteSpace(r.From) && !string.IsNullOrWhiteSpace(r.To))
        .GroupBy(r => r.From, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().To, StringComparer.OrdinalIgnoreCase);
    
    // Apply to the loaded route file so one ocelot.json serves both localhost dev
    // and the docker-compose network. Keys are full "host:port" authorities:
    // a host-only rewrite would collapse every service (all entries say
    // "localhost", differing only by port) onto one container with ports nothing
    // listens on. Values are either a bare "host:port" (rewrite keeps the route's
    // own scheme — the docker-compose form) or scheme-qualified
    // "http(s)://host[:port]" (Issue #78: Render addresses services by public
    // https URLs; the port may be omitted and defaults to the scheme's — 443
    // for https, 80 for http). A scheme-qualified To also rewrites the route's
    // DownstreamScheme. See GatewayRewrites for the exact grammar.
    var ocelotJson = JObject.Parse(File.ReadAllText(
        Path.Combine(builder.Environment.ContentRootPath, "ocelot.json")));
    var warnedRewrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var route in ocelotJson["Routes"]?.Children() ?? Enumerable.Empty<JToken>())
    {
        foreach (var hp in route["DownstreamHostAndPorts"]?.Children() ?? Enumerable.Empty<JToken>())
        {
            var host = hp["Host"]?.Value<string>();
            var port = hp["Port"]?.Value<string>() ?? "";
            if (host == null)
            {
                continue;
            }
            if (rewrites.TryGetValue($"{host}:{port}", out var replacement))
            {
                if (GatewayRewrites.TryParseTarget(replacement, out var newScheme, out var newHost, out var newPort))
                {
                    hp["Host"] = newHost;
                    hp["Port"] = newPort;
                    if (newScheme != null)
                    {
                        // Single-entry-per-route assumed: every route in
                        // ocelot.json carries exactly one DownstreamHostAndPorts
                        // object. Do NOT hard-code the route count here -- the
                        // previous comment said "all 36 routes" when the file
                        // held 35, and survived the 35 -> 34 change in #127
                        // (git log -S on this file, not a guess). This loop
                        // visits every entry, so a second one is not a broken
                        // rewrite but an unrewritten one: an entry whose
                        // authority the table omits keeps its localhost address
                        // while its sibling is redirected, silently splitting
                        // one route across two destinations. If you add a
                        // second entry, grep DownstreamHostAndPorts across the
                        // routes first.
                        route["DownstreamScheme"] = newScheme;
                    }
                }
                else
                {
                    // Many routes share one authority, so warn once per From.
                    if (warnedRewrites.Add($"{host}:{port}"))
                    {
                        Log.Warning("Ignoring Gateway:Rewrites entry {From} -> {To}: unparseable target",
                            $"{host}:{port}", replacement);
                    }
                }
            }
        }
    }
    
    var ocelotConfiguration = new ConfigurationBuilder()
        .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ocelotJson.ToString())))
        .Build();
    
    builder.Services.AddOcelot(ocelotConfiguration);
    
    // Aggregate health endpoint: pings every service's /health in parallel.
    builder.Services.AddSingleton<HcHealthCheckWriter>();
    builder.Services.AddHttpClient("health", c => c.Timeout = TimeSpan.FromSeconds(3));
    
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend",
            policy =>
            {
                // Allowed origins come from config as one comma-separated string
                // (Cors__AllowedOrigins="https://a,https://b") so a deployed
                // frontend (e.g. the Vercel app) can be permitted without a
                // rebuild. Default keeps the Vite dev ports working unchanged.
                // Origins must be exact "scheme://host[:port]" values: no
                // trailing slash and no wildcard patterns (WithOrigins stores
                // them verbatim, so e.g. "https://*.vercel.app" silently never
                // matches). AllowCredentials below forbids a "*" origin, hence
                // the list.
                var defaultOrigins = new[]
                {
                    "http://localhost:5173", "http://localhost:5174", "http://localhost:5175",
                };
                var configured = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var origins = configured.Length > 0 ? configured : defaultOrigins;
                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
    });
    
    var app = builder.Build();
    
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    
    // NOTE: no UseHttpsRedirection - the gateway is served over http here
    // (launchSettings exposes http://localhost:5036) and a redirect would break
    // plain-http callers; TLS termination is deployment's job, not the gateway's.
    
    // Enable CORS - must be before UseOcelot()
    app.UseCors("AllowFrontend");
    
    // ORDER MATTERS: app.Map() branches match by path PREFIX
    // (PathString.StartsWithSegments), not exact equality, and branch.Run()
    // is terminal — there is no fallthrough. So /health/live MUST be
    // registered BEFORE /health, or the "/health" prefix swallows it and
    // the liveness route answers the aggregate's 503 instead. Probed: with
    // the branches in the wrong order /health/live returned the aggregate
    // body and a 503, i.e. the fix was a silent no-op. A more specific path
    // always goes first under this branching style.

    // Liveness WITHOUT the aggregate dependency. The /health branch below
    // returns 503 unless every upstream answers 2xx (Program.cs), which
    // makes it a READINESS signal, not a liveness one. Render's health check
    // (render.yaml healthCheckPath) requires 2xx/3xx and CANCELS a deploy
    // whose checks stay failing past 15 minutes — so pointing it at the
    // aggregate would make the gateway's deploy hinge on all six upstreams
    // being healthy within one window, with no existing instance to fall
    // back to on a first `render blueprint apply`. (The broker note in
    // render.yaml is directionally right but imprecise: SalesService's
    // RabbitMQMessagePublisher is an AddSingleton that nothing resolves at
    // startup, so its ctor — which rethrows on an unreachable broker — only
    // runs on the FIRST controller request, not at boot, and /health answers
    // fine with the broker down. The eager ones are CustomerService's and
    // NotificationService's consumers: CustomerService is
    // AddHostedService<VehicleReservedEventConsumer> (its ExecuteAsync catch
    // logs WITHOUT rethrowing, so a boot-time failure ends the hosted service
    // permanently — /health stays 200 while the consumer is dead), and
    // NotificationService is AddHostedService<RabbitMQConsumerHostedService>
    // whose StartAsync calls StartConsuming() — which returns silently when
    // the connection is not open (its InitializeRabbitMQ catch swallows),
    // while NotificationService's /health is a hardcoded 200 with no broker
    // check at all. Either way the conclusion stands: the gateway's deploy
    // gate must not depend on upstream readiness.
    // /health/live answers 200 once the process has built its pipeline and
    // reached app.Run() — which is all a deploy gate can honestly require of
    // a routing gateway. (Not "the moment the process is up": nothing answers
    // before app.Run() binds the port, which follows the awaited UseOcelot()
    // below. The builder-phase work — the ocelot.json rewrite loop and the
    // Serilog bootstrap — runs earlier still, so it cannot delay listening.)
    // The Docker HEALTHCHECK stays on /health with `curl -s` (any status
    // proves the process answers) and the aggregate /health remains the
    // readiness/monitoring view.
    app.Map("/health/live", branch => branch.Run(async context =>
    {
        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "healthy",
            service = "apigateway",
            timestamp = DateTime.UtcNow
        });
    }));

    // Aggregate readiness endpoint: pings every service's /health in parallel.
    // Must stay a Map() branch BEFORE UseOcelot: Ocelot's responder
    // short-circuits unknown paths with its own 404, so an endpoint route
    // mapped after it never runs.
    app.Map("/health", branch => branch.Run(async context =>
    {
        var writer = context.RequestServices.GetRequiredService<HcHealthCheckWriter>();
        var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("health");

        var results = await Task.WhenAll(writer.Upstreams.Select(async t =>
        {
            try
            {
                using var resp = await client.GetAsync(t.HealthUrl);
                return new GatewayServiceHealth(t.Service, resp.IsSuccessStatusCode ? "healthy" : "degraded", (int)resp.StatusCode, null);
            }
            catch (Exception ex)
            {
                return new GatewayServiceHealth(t.Service, "unreachable", 0, ex.GetType().Name);
            }
        }));

        var healthy = results.Count(r => r.status == "healthy");
        context.Response.StatusCode = healthy == results.Length ? 200 : 503;
        await context.Response.WriteAsJsonAsync(new
        {
            gateway = "healthy",
            services = results,
            summary = $"{healthy}/{results.Length} services healthy",
            timestamp = DateTime.UtcNow
        });
    }));
    
    // Use Ocelot middleware
    await app.UseOcelot();
    
    app.Run();
}
catch (Exception ex)
{
    // Rethrow, matching the other six services since #90 (VehicleService was
    // already this shape): AddOcelot/UseOcelot or a missing ocelot.json
    // (JObject.Parse above) otherwise exits the process with code 0 and no
    // listener — indistinguishable from a clean shutdown to a crash-loop or
    // deploy gate, which is exactly what /health/live exists to inform. The
    // finally block still flushes Serilog before the exception escapes.
    Log.Fatal(ex, "APIGatewayService failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

internal sealed record GatewayServiceHealth(string service, string status, int? httpStatus, string? error);

/// <summary>
/// One Gateway:Rewrites entry: full downstream authority (host:port) to
/// replace, and its replacement. Colon can't live in a config key, hence
/// the list-of-pairs shape.
/// </summary>
internal sealed class GatewayRewrite
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

/// <summary>
/// Parses a Gateway:Rewrites "To" value (Issue #78). Accepted forms:
///   "host:port"               -> no scheme (null; caller keeps the route's),
///                                 the docker-compose form — behavior unchanged
///   "http(s)://host[:port]"   -> scheme returned separately; port defaults to
///                                 443/80 when omitted (Render public URLs)
/// Everything else (bare "https://", non-numeric port, empty host) is false —
/// callers drop malformed entries rather than crash, matching the existing
/// whitespace-filter convention.
/// </summary>
internal static class GatewayRewrites
{
    public static bool TryParseTarget(string? to, out string? scheme, out string host, out int port)
    {
        scheme = null;
        host = "";
        port = 0;
        if (string.IsNullOrWhiteSpace(to))
        {
            return false;
        }

        var value = to.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "http";
            value = value["http://".Length..];
        }
        else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "https";
            value = value["https://".Length..];
        }

        // Trailing slashes are stripped, so "https://host/" is tolerated; a
        // remaining '/' at that point means a path/query snuck into a rewrite
        // target — reject it.
        value = value.TrimEnd('/');
        if (value.Contains('/'))
        {
            return false;
        }
        if (value.Length == 0)
        {
            return false;
        }

        // IPv6 literals would need bracket handling; this stack never uses them
        // (services are addressed by DNS name), so ':' > once is malformed.
        var parts = value.Split(':', 2);
        host = parts[0];
        if (host.Length == 0)
        {
            return false;
        }
        if (parts.Length == 1)
        {
            // Scheme-qualified values may omit the port; bare ones may not,
            // because "host" alone would collapse every service (see above).
            port = scheme switch
            {
                "https" => 443,
                "http" => 80,
                _ => 0,
            };
            return port != 0;
        }
        return int.TryParse(parts[1], out port) && port is > 0 and <= 65535;
    }
}

/// <summary>
/// Where the gateway pings each service's /health. Mirrors the
/// host:port authority table in ocelot.json and applies the same
/// Gateway:Rewrites rewrite (including Issue #78 scheme-qualified targets,
/// whose scheme the probe follows), so the probe list tracks the route file
/// into docker-compose or Render addressing.
/// </summary>
internal sealed class HcHealthCheckWriter
{
    private static readonly (string Service, string Authority)[] Services =
    {
        ("user", "localhost:7001"),
        ("vehicle", "localhost:5068"),
        ("sales", "localhost:5003"),
        ("customer", "localhost:5039"),
        ("reporting", "localhost:5208"),
        ("notification", "localhost:5051"),
    };

    public IReadOnlyList<(string Service, string HealthUrl)> Upstreams { get; }

    public HcHealthCheckWriter(IConfiguration configuration)
    {
        var rewrites = (configuration.GetSection("Gateway:Rewrites").Get<List<GatewayRewrite>>()
            ?? new List<GatewayRewrite>())
            .Where(r => !string.IsNullOrWhiteSpace(r.From) && !string.IsNullOrWhiteSpace(r.To))
            .GroupBy(r => r.From, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().To, StringComparer.OrdinalIgnoreCase);
        Upstreams = Services.Select(s =>
        {
            // Unset or unparseable rewrites fall back to the dev
            // "http://localhost:port" probe; the ocelot routes stay untouched
            // either way. (Issue #78 note: before the shared parser, an
            // unparseable bare-host To like "userservice" was probed verbatim
            // here while routes ignored it — probing the dev authority now is
            // the deliberate change.)
            if (rewrites.TryGetValue(s.Authority, out var target)
                && GatewayRewrites.TryParseTarget(target, out var scheme, out var host, out var port))
            {
                return (s.Service, $"http{(scheme == "https" ? "s" : "")}://{host}:{port}/health");
            }
            return (s.Service, $"http://{s.Authority}/health");
        }).ToList();
    }
}
