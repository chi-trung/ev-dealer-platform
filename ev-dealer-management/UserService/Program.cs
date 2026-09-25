using Serilog;
using Common.Data;
using Common.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Linq;
using UserService.Data;
using UserService.Endpoints;
using UserService.Services;

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
    Log.Information("Starting UserService...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog(); // Issue #63: route all ILogger<T> through the static Serilog logger above
    
    // Configuration sections
    builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                       .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                       .AddEnvironmentVariables();
    
    // Add services
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks()
        // #135: an empty AddHealthChecks() answers 200 unconditionally.
        // UserService has no broker client, so /health reports its one real
        // dependency only. The JSON writer below carries per-check detail so
        // a 503 explains which dependency is down instead of the bare
        // "Unhealthy" string the framework writes by default.
        .AddDatabaseCheck<UserDbContext>();
    builder.Services.AddAuthorization();
    builder.Services.AddAuthentication();
    
    // CORS
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
    
    // Issue #89: provider switch is centralised in Common.DbProviderSelector
    // (DB_PROVIDER=postgres → UseNpgsql, unset/sqlite → UseSqlite, exactly the
    // connection-string behaviour this line had before).
    builder.Services.AddApplicationDbContext<UserDbContext>(
        builder.Configuration,
        sqliteFallback: "Data Source=users.db");
    
    // Authentication - JWT
    var jwtSection = builder.Configuration.GetSection("Jwt");
    var jwtKey = jwtSection.GetValue<string>("Key") ?? "ReplaceThisWithASecretKeyForDevelopment";
    var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
    
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "JwtBearer";
        options.DefaultChallengeScheme = "JwtBearer";
    })
    .AddJwtBearer("JwtBearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection.GetValue<string>("Issuer") ?? "evm.local",
            ValidAudience = jwtSection.GetValue<string>("Audience") ?? "evm.local",
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
        };
    });
    
    // Add minimal services
    builder.Services.AddScoped<IUserService, UserServiceImpl>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddLogging();

    // Issue #121: UserService no longer owns the Dealers table, so DealerId
    // validation goes over HTTP to VehicleService. Registering this as a
    // singleton keeps a short in-memory cache -- GET /api/dealers per
    // registration is otherwise a network round-trip on every signup.
    builder.Services.AddSingleton<DealerIdValidator>();
    builder.Services.AddHttpClient<DealerIdValidator>(c =>
    {
        var baseUri = builder.Configuration["Services:VehicleService"];
        if (!string.IsNullOrWhiteSpace(baseUri))
            c.BaseAddress = new Uri(baseUri.EndsWith('/') ? baseUri : baseUri + "/");
    });
    
    
    var app = builder.Build();
    
    // Apply migrations and seed data at startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
        // Issue #121: the Dealers seed moved to VehicleService, which owns the
        // table (VehicleService/ApplicationDbContext.cs SeedData, now applied
        // by that service's Baseline migration). Seeding it here would require
        // the DbSet back, which is exactly the shared-table collision this
        // change removes.
        // Fail-soft ONLY for transient/locking faults, not schema faults
        // (review round 3). A blanket catch (Exception) here would swallow a
        // real migration error -- under Postgres that is PostgresException
        // 42P07 duplicate_table / 42701 duplicate_column / 42P16
        // invalid_table_definition -- and leave the service booting green with
        // every endpoint dying on "no such table". That is exactly the silent
        // failure mode this whole issue was about, and it is worse than a
        // crashloop, because nothing visible signals it. Schema errors must
        // fail loudly and take the process down; only lock contention and
        // transient connection failures are recoverable.
        try
        {
            db.Database.Migrate();
        }
        catch (Exception ex) when (IsTransientMigrationFault(ex))
        {
            Console.Error.WriteLine($"[UserService] Warning: transient database migration failure (existing schema assumed): {ex.Message}");
        }

        // Npgsql surfaces a SqlException whose Number is the Postgres error
        // code (e.g. 40P01 deadlock, 55P03 lock_not_available); Microsoft.Data
        // .Sqlite surfaces "database is locked" by message. Anything else --
        // a schema error -- is NOT transient and must not be swallowed.
        // Local function because this file uses top-level statements, which
        // cannot hold method declarations.
        static bool IsTransientMigrationFault(Exception ex)
        {
            for (var e = ex; e is not null; e = e.InnerException)
            {
                if (e is Microsoft.Data.Sqlite.SqliteException sx)
                    return sx.Message.Contains("locked", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
    
    // Configure middleware
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    
    app.UseHttpsRedirection();
    app.UseCors("AllowFrontend");
    app.UseAuthentication();
    app.UseAuthorization();
    
    // Liveness probe for the API gateway aggregate /health (docs/GATEWAY.md).
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = BrokerHealthCheckExtensions.WriteHealthReportAsync
    });
    
    // All 15 routes moved verbatim to Endpoints/UserEndpoints.cs (P1 split).
    // Keeping the call here means the composition root still owns the
    // routing surface without re-listing it.
    app.MapUserEndpoints();
    
    
    app.Run();
}
catch (Exception ex)
{
    // Rethrow: AddApplicationDbContext's hard-fail (bad/missing DB config) must
    // kill the process with a non-zero exit, not be swallowed here into a
    // Fatal log line and an exit code of 0 — a restart-loop health gate would
    // see nothing actionable. See Common/DbProviderSelector.cs (issue #89).
    Log.Fatal(ex, "UserService failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
