using Serilog;
using Common.Auth;
using Common.Data;
using Common.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SalesService.Data;
using SalesService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using QuestPDF.Infrastructure; // Required for LicenseType
using System.Text.Json.Serialization; // Required for ReferenceHandler

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
    Log.Information("Starting SalesService...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog(); // Issue #63: route all ILogger<T> through the static Serilog logger above
    
    // Configure QuestPDF license (with error handling for Docker/Linux)
    try
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }
    catch (Exception ex)
    {
        // Log error but don't fail startup - QuestPDF is only used for PDF generation
        Console.WriteLine($"[WARNING] Failed to initialize QuestPDF: {ex.Message}");
        Console.WriteLine("[INFO] Service will continue without PDF generation support");
    }
    
    // Add CORS
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
    
    // Add services to the container.
    // Configure JSON serialization to handle object cycles
    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve;
    });
    
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks()
        // #135: an empty AddHealthChecks() answers 200 unconditionally. The
        // broker probe matters most for THIS service: its publisher's ctor
        // THROWS when the broker is unreachable, so a misconfigured
        // RabbitMQ__* takes the whole process down (the #90 rethrow then
        // makes that a non-zero exit). When the publisher did construct, the
        // probe is what tells an operator the broker is the cause.
        .AddBrokerProbeCheck()
        .AddDatabaseCheck<SalesDbContext>();
    
    // Issue #89: provider switch centralised in Common.DbProviderSelector.
    // The .LogTo/.EnableSensitiveDataLogging that was chained onto UseSqlite
    // here is provider-neutral, so it goes in the callback and survives the
    // switch to UseNpgsql.
    builder.Services.AddApplicationDbContext<SalesDbContext>(
        builder.Configuration,
        sqliteFallback: "Data Source=sales.db",
        configureOptions: options => options
            .LogTo(Console.WriteLine, LogLevel.Information)
            .EnableSensitiveDataLogging());
    
    // Register RabbitMQ Message Publisher
    builder.Services.AddSingleton<IMessagePublisher, RabbitMQMessagePublisher>();

    // JWT authentication (Issue #137): SalesService never registered auth at
    // all, so every order/quote/contract/payment/promotion/delivery endpoint
    // was anonymous -- 16 mutation routes reachable through the gateway by
    // anyone. Same validation parameters as UserService/CustomerService/
    // NotificationService: tokens are minted only by UserService's
    // /api/auth/login, and the Jwt__* trio must byte-match across services or
    // a token one service issued is rejected by the others.
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
    
    // Apply migrations at startup (same pattern as UserService). The bind-mounted
    // data/ dir is empty on a fresh clone, and without a schema every endpoint
    // 500s with `sqlite_error(no such table: Orders)`. Pre-squash dev DBs (history
    // rows for the migrations deleted in 73ce679, schema already present) make
    // Migrate() throw "table already exists" — safe to ignore, same fail-soft as
    // ReportingService — and fresh DBs get migrated normally.
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            scope.ServiceProvider.GetRequiredService<SalesDbContext>().Database.Migrate();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SalesService] Warning: could not apply database migrations (existing schema assumed): {ex.Message}");
        }
    }
    
    // Log the database file path after app is built
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var dbContext = services.GetRequiredService<SalesDbContext>();
            var connection = dbContext.Database.GetDbConnection();
            if (connection is SqliteConnection sqliteConnection)
            {
                Console.WriteLine($"[SalesDbContext] SQLite database file path: {sqliteConnection.DataSource}");
            }
            else
            {
                Console.WriteLine($"[SalesDbContext] Database connection type: {connection.GetType().Name}. Could not determine SQLite file path.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SalesDbContext] Error getting database context or connection string: {ex.Message}");
        }
    }
    
    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    
    app.UseHttpsRedirection();
    
    // Use CORS
    app.UseCors("AllowFrontend");

    // Issue #137: auth must run before MapControllers -- attribute metadata
    // on the actions is what [Authorize]/[AllowAnonymous] resolve against.
    app.UseAuthentication();
    // Issue #92 (P2): between UseAuthentication and UseAuthorization so a
    // promoted identity is in place BEFORE authorization evaluates it.
    // ReportingService's data-sync fan-out is machine-to-machine traffic with
    // no user JWT, and [Authorize] on the controllers below was rejecting every
    // one of those calls with 401 -- which the fan-out logged as a warning and
    // reported to its caller as an empty result.
    app.UseInternalServiceAuth();
    app.UseAuthorization();

    app.MapControllers();
    
    // Liveness probe for the API gateway aggregate /health (docs/GATEWAY.md).
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = BrokerHealthCheckExtensions.WriteHealthReportAsync
    });
    
    app.Run();
}
catch (Exception ex)
{
    // Rethrow: AddApplicationDbContext's hard-fail (bad/missing DB config) must
    // kill the process with a non-zero exit, not be swallowed here into a
    // Fatal log line and an exit code of 0 — a restart-loop health gate would
    // see nothing actionable. See Common/DbProviderSelector.cs (issue #89).
    Log.Fatal(ex, "SalesService failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
