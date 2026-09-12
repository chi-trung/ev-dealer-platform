using Microsoft.AspNetCore.Builder;
using Newtonsoft.Json.Linq;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

var hostRewrites = builder.Configuration
    .GetSection("Gateway:Hosts")
    .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();

// Rewrite downstream hosts in the loaded route file so one ocelot.json serves
// both localhost dev and the docker-compose network (Gateway:Hosts maps
// "localhost" -> "userservice"-style container names there).
var ocelotJson = JObject.Parse(File.ReadAllText(
    Path.Combine(builder.Environment.ContentRootPath, "ocelot.json")));
foreach (var route in ocelotJson["Routes"]?.Children() ?? Enumerable.Empty<JToken>())
{
    foreach (var hp in route["DownstreamHostAndPorts"]?.Children() ?? Enumerable.Empty<JToken>())
    {
        var host = hp["Host"]?.Value<string>();
        if (host != null && hostRewrites.TryGetValue(host, out var replacement))
        {
            hp["Host"] = replacement;
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

internal sealed record GatewayServiceHealth(string service, string status, int? httpStatus, string? error);

/// <summary>
/// Where the gateway pings each service's /health. Ports mirror the
/// localhost:port table in ocelot.json; the host follows the Gateway:Hosts
/// rewrite so the same list works in docker-compose (service names) and dev
/// (localhost).
/// </summary>
internal sealed class HcHealthCheckWriter
{
    private static readonly (string Service, int Port)[] Services =
    {
        ("user", 7001),
        ("vehicle", 5068),
        ("sales", 5003),
        ("customer", 5039),
        ("reporting", 5208),
        ("notification", 5051),
    };

    public IReadOnlyList<(string Service, string HealthUrl)> Upstreams { get; }

    public HcHealthCheckWriter(IConfiguration configuration)
    {
        var hostRewrites = configuration.GetSection("Gateway:Hosts").Get<Dictionary<string, string>>();
        var host = hostRewrites?.TryGetValue("localhost", out var h) == true ? h : "localhost";
        Upstreams = Services.Select(s => (s.Service, $"http://{host}:{s.Port}/health")).ToList();
    }
}
