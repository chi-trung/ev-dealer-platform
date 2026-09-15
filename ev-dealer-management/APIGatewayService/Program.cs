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
    // and the docker-compose network. Keys/values are full "host:port" authorities:
    // a host-only rewrite would collapse every service (all entries say
    // "localhost", differing only by port) onto one container with ports nothing
    // listens on.
    var ocelotJson = JObject.Parse(File.ReadAllText(
        Path.Combine(builder.Environment.ContentRootPath, "ocelot.json")));
    foreach (var route in ocelotJson["Routes"]?.Children() ?? Enumerable.Empty<JToken>())
    {
        foreach (var hp in route["DownstreamHostAndPorts"]?.Children() ?? Enumerable.Empty<JToken>())
        {
            var host = hp["Host"]?.Value<string>();
            var port = hp["Port"]?.Value<string>() ?? "";
            if (host != null
                && rewrites.TryGetValue($"{host}:{port}", out var replacement)
                && replacement.Split(':', 2) is [var newHost, var newPort])
            {
                hp["Host"] = newHost;
                hp["Port"] = int.Parse(newPort);
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
                // Allow common dev ports used by Vite (5173, 5174, 5175)
                policy.WithOrigins("http://localhost:5173", "http://localhost:5174", "http://localhost:5175")
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
    
    // Liveness endpoint. Must be a Map() branch BEFORE UseOcelot: Ocelot's
    // responder short-circuits unknown paths with its own 404, so an endpoint
    // route mapped after it never runs. Aggregate: pings every service's /health.
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
    Log.Fatal(ex, "APIGatewayService failed to start");
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
/// Where the gateway pings each service's /health. Mirrors the
/// host:port authority table in ocelot.json and applies the same
/// Gateway:Rewrites "host:port" -> "host:port" rewrite, so the probe list
/// follows the route file into docker-compose addressing.
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
            var authority = rewrites.TryGetValue(s.Authority, out var r) ? r : s.Authority;
            return (s.Service, $"http://{authority}/health");
        }).ToList();
    }
}
