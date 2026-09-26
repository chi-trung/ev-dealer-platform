using Serilog;
using Common.Auth;
using Common.Data;
using Common.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ev_dealer_reporting.Data;
using ev_dealer_reporting.Endpoints;
using System.IO;
using ev_dealer_reporting.Models;
using ev_dealer_reporting.Services;
using ev_dealer_reporting.DTOs;
using Microsoft.Extensions.Logging; // Add this for ILogger

// Issue #63: Serilog bootstrap — the convention NotificationService has
// run since well before this repo’s CI era: sinks configured from
// appsettings.json, console startup failures also land as Fatal in the
// daily-rolling file (mounted at /app/Logs), CloseAndFlush on exit.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build())
    .CreateLogger();

try
{
    Log.Information("Starting ReportingService...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog(); // Issue #63: route all ILogger<T> through the static Serilog logger above
    
    // Load configuration files and environment
    builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                       .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                       .AddEnvironmentVariables();
    
    // Add services to the container.
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks()
        // #135: this service's /health was bare MapHealthChecks with NO
        // registered checks, so it answered 200 for a process whose DB was
        // unreachable — while Migrate() at startup had already hard-failed it.
        // ReportingService has no broker client of its own (it pulls the other
        // services over HTTP), so only its own DB is probed.
        .AddDatabaseCheck<ReportingDbContext>();
    
    // Add custom services
    builder.Services.AddScoped<IForecastingService, ForecastingService>();
    builder.Services.AddScoped<IReportService, ReportService>(); // Register ReportService
    
    // Register HttpClient for typed clients
    //
    // Issue #92 (P2): .AddHttpMessageHandler(InternalServiceKeyHandler) makes
    // every outbound request carry the shared internal key, so sibling
    // services can authenticate this machine-to-machine traffic. Without it
    // the [Authorize] added in #137 answered 401 to all thirteen call sites
    // and the sync endpoint reported success while writing nothing.
    // Registered here rather than at each call site so a call added later
    // cannot forget it.
    //
    // Transient is load-bearing: AddHttpMessageHandler<T>() resolves the
    // handler on every pipeline build, and a DelegatingHandler handed out
    // twice throws "The 'InnerHandler' property must be null ... must not be
    // reused or cached" because the factory sets InnerHandler itself.
    builder.Services.AddTransient<InternalServiceKeyHandler>();
    builder.Services.AddHttpClient<ISalesDataService, SalesDataService>()
        .AddHttpMessageHandler<InternalServiceKeyHandler>();
    builder.Services.AddHttpClient<IVehicleDataService, VehicleDataService>()
        .AddHttpMessageHandler<InternalServiceKeyHandler>();
    builder.Services.AddHttpClient<ICustomerDataService, CustomerDataService>() // Register CustomerDataService
        .AddHttpMessageHandler<InternalServiceKeyHandler>();
    builder.Services.AddHttpClient<IUserDataService, UserDataService>() // Register UserDataService
        .AddHttpMessageHandler<InternalServiceKeyHandler>();
    
    // Register the new data synchronization service
    builder.Services.AddScoped<IDataSynchronizationService, DataSynchronizationService>();
    
    
    // CORS - allow local frontend during development
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            // Origins come from config as one comma-separated string
            // (Cors__AllowedOrigins="https://a,https://b") so a deployed
            // frontend (e.g. the Vercel app) can be permitted without a
            // rebuild. Default keeps the Vite dev ports working unchanged.
            // Origins must be exact "scheme://host[:port]" values: no
            // trailing slash and no wildcard patterns (WithOrigins stores
            // them verbatim, so e.g. "https://*.vercel.app" silently never
            // matches). Same pattern as APIGatewayService (Issue #77).
            // Vite defaults to 5173 and increments (5174, 5175...) when the
            // port is busy, so all three are listed to match the gateway's
            // defaults exactly. 3000 covers a plain `react-scripts` boot.
            var defaultOrigins = new[]
            {
                "http://localhost:5173",
                "http://localhost:5174",
                "http://localhost:5175",
                "http://localhost:3000",
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
    
    // Issue #89: provider switch centralised in Common.DbProviderSelector —
    // the same switch the other five services use. This replaces the previous
    // bespoke USE_SQLITE + probe-and-fall-back-to-SQLite block, which was the
    // exact pattern the shared helper was written to avoid: a Postgres
    // instance that is merely down at boot made the service silently serve
    // SQLite instead, and every report row written to the container layer was
    // lost on the next recreate.
    //
    // The SQLite fallback still honours REPORTING_DB_PATH (compose points it
    // at the /app/data volume) so existing local and compose behaviour is
    // unchanged on the default DB_PROVIDER=sqlite.
    var sqlitePath = Environment.GetEnvironmentVariable("REPORTING_DB_PATH");
    if (string.IsNullOrWhiteSpace(sqlitePath))
        sqlitePath = Path.Combine(AppContext.BaseDirectory, "reporting_dev.db");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sqlitePath))!);
    builder.Services.AddApplicationDbContext<ReportingDbContext>(
        builder.Configuration,
        sqliteFallback: $"Data Source={sqlitePath}");

    // JWT authentication (Issue #137): ReportingService never registered auth,
    // so /api/reports/synchronize-data, /api/reports/export and the two
    // POST summary endpoints were anonymous -- 4 mutation routes. Same
    // validation parameters as the services that already validate: tokens are
    // minted only by UserService's /api/auth/login, so the Jwt__* trio must
    // byte-match across services.
    var jwtSection = builder.Configuration.GetSection("Jwt");
    var jwtKey = jwtSection.GetValue<string>("Key") ?? "ReplaceThisWithASecretKeyForDevelopment";
    var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

    builder.Services.AddAuthentication("JwtBearer")
        .AddJwtBearer("JwtBearer", options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSection.GetValue<string>("Issuer"),
                ValidAudience = jwtSection.GetValue<string>("Audience"),
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
            };
        });
    builder.Services.AddAuthorization();

    var app = builder.Build();
    
    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    
    app.UseHttpsRedirection();
    app.UseCors("AllowFrontend");

    // Issue #137: auth before the route registrations below -- the minimal-API
    // .RequireAuthorization() calls attach to endpoint metadata resolved when
    // the endpoints are built, and UseAuthentication/UseAuthorization must be
    // in the pipeline before requests reach them.
    app.UseAuthentication();
    app.UseAuthorization();
    
    // Liveness probe for the API gateway aggregate /health (docs/GATEWAY.md).
    // The JSON writer carries per-check detail so a 503 explains WHICH
    // dependency is down (#135) instead of the framework's bare "Unhealthy".
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = BrokerHealthCheckExtensions.WriteHealthReportAsync
    });
    
    // Apply database migrations and ensure database is created
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            db.Database.Migrate();
            await EnsureRegionDataAsync(db);
    
            // Trigger initial data synchronization after migrations
            var dataSyncService = scope.ServiceProvider.GetRequiredService<IDataSynchronizationService>();
            await dataSyncService.SynchronizeAllDataAsync();
        }
        catch (Exception ex)
        {
            // Log or ignore for now
            Console.Error.WriteLine($"Warning: could not apply database migrations or synchronize data: {ex.Message}");
        }
    }
    
    // All 19 routes moved verbatim to Endpoints/ReportEndpoints.cs (P1 split).
    // The call stays here so the composition root still owns the routing surface.

    app.MapReportEndpoints();
    
    static async Task EnsureRegionDataAsync(ReportingDbContext db)
    {
        var dealerRegionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dealer Hà Nội"] = "Miền Bắc",
            ["Dealer TP.HCM"] = "Miền Nam",
            ["Dealer Đà Nẵng"] = "Miền Trung",
        };
    
        var salesWithoutRegion = await db.SalesSummaries
            .Where(s => string.IsNullOrWhiteSpace(s.Region))
            .ToListAsync();
    
        foreach (var sale in salesWithoutRegion)
        {
            if (dealerRegionMap.TryGetValue(sale.DealerName, out var region))
            {
                sale.Region = region;
            }
        }
    
        var inventoryWithoutRegion = await db.InventorySummaries
            .Where(i => string.IsNullOrWhiteSpace(i.Region))
            .ToListAsync();
    
        foreach (var inv in inventoryWithoutRegion)
        {
            if (dealerRegionMap.TryGetValue(inv.DealerName, out var region))
            {
                inv.Region = region;
            }
        }
    
        var updated = salesWithoutRegion.Any(s => !string.IsNullOrWhiteSpace(s.Region)) ||
                      inventoryWithoutRegion.Any(i => !string.IsNullOrWhiteSpace(i.Region));
    
        if (updated)
        {
            await db.SaveChangesAsync();
        }
    }
    app.Run();
}
catch (Exception ex)
{
    // Rethrow: AddApplicationDbContext's hard-fail (bad/missing DB config) must
    // kill the process with a non-zero exit, not be swallowed here into a
    // Fatal log line and an exit code of 0 — a restart-loop health gate would
    // see nothing actionable. See Common/DbProviderSelector.cs (issue #89).
    Log.Fatal(ex, "ReportingService failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}