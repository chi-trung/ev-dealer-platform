using Serilog;
using Common.Data;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NotificationService.Services;
using NotificationService.Consumers;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build())
    .CreateLogger();

try
{
    Log.Information("Starting NotificationService...");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

    // Add services to the container.
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Register Firebase FCM Service (Singleton for better performance)
    builder.Services.AddSingleton<IFcmService, FirebaseFcmService>();

    // DeviceToken registry (Issue #33): the only database this service owns.
    // Same connection-string convention as the other services — config /
    // ConnectionStrings__DefaultConnection wins so docker-compose can relocate
    // it onto a mounted volume; otherwise the file sits in ContentRootPath.
    // Issue #89: provider switch centralised in Common.DbProviderSelector.
    // Same connection-string convention as before: config wins, else the
    // ContentRootPath/notifications.db fallback.
    builder.Services.AddApplicationDbContext<NotificationService.Data.NotificationDbContext>(
        builder.Configuration,
        sqliteFallback: $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "notifications.db")}");
    builder.Services.AddScoped<IDeviceTokenRegistry, DeviceTokenRegistry>();
    builder.Services.AddScoped<INotificationPreferencesStore, NotificationPreferencesStore>();
    // Issue #56: the read-side of preferences — turns #51's stored documents
    // into delivery decisions at user:<id> fan-out points (Quote/Contract
    // consumers' salesperson audience). Stateless over the store; Scoped
    // matches the store's lifetime.
    builder.Services.AddScoped<INotificationPreferencePolicy, NotificationPreferencePolicy>();

    // JWT authentication (Issue #36): DeviceTokens registration is now
    // authenticated. Same validation params as CustomerService/UserService —
    // tokens are minted by UserService (/api/auth/login) with claims
    // id / unique_name / role (+ dealer when the account has a DealerId).
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

    // Register Consumers
    builder.Services.AddScoped<SaleCompletedConsumer>();
    builder.Services.AddScoped<VehicleReservedConsumer>();
    builder.Services.AddScoped<TestDriveScheduledConsumer>();
    builder.Services.AddScoped<OrderCreatedConsumer>();
    builder.Services.AddScoped<QuoteCreatedConsumer>();
    builder.Services.AddScoped<ContractCreatedConsumer>();
    builder.Services.AddScoped<CustomerCreatedConsumer>();
    builder.Services.AddScoped<CustomerUpdatedConsumer>();
    builder.Services.AddScoped<CustomerDeletedConsumer>();
    builder.Services.AddScoped<PaymentReceivedConsumer>();
    builder.Services.AddScoped<OrderStatusChangedConsumer>();
    builder.Services.AddScoped<VehicleCreatedConsumer>();
    builder.Services.AddScoped<VehicleUpdatedConsumer>();
    builder.Services.AddScoped<VehicleDeletedConsumer>();

    // Register RabbitMQ Consumer Service
    builder.Services.AddSingleton<IMessageConsumer, RabbitMQConsumerService>();
    builder.Services.AddHostedService<RabbitMQConsumerHostedService>();

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
            // defaults exactly.
            var defaultOrigins = new[]
            {
                "http://localhost:5173",
                "http://localhost:5174",
                "http://localhost:5175",
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

    // Add Controllers
    builder.Services.AddControllers();

    var app = builder.Build();

    // Create the registry schema on boot. Small tables with no history to
    // replay, so EnsureCreated is honest here (the other services ship
    // migrations and use Migrate()). Fail-soft on a locked/corrupt db file —
    // a dead registry must not take the 14 queue consumers down with it;
    // events then degrade to log-only, the pre-Issue-#33 behavior.
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationService.Data.NotificationDbContext>();
        db.Database.EnsureCreated();
        // EnsureCreated never diffs an existing database, so a local SQLite
        // file from before Issue #51 has DeviceTokens only — the second table
        // ships as explicit IF NOT EXISTS DDL matching the EF mapping.
        // Idempotent on fresh databases (EnsureCreated already made it) and on
        // existing ones. Identity, boolean and timestamp types differ by
        // provider (Issue #91). Keep this manual DDL aligned with the model's
        // GenerateCreateScript output, including UTC timestamp storage.
        var isSqlite = db.Database.ProviderName!.EndsWith("Sqlite", StringComparison.Ordinal);
        var prefsTable = isSqlite
            ? """
            CREATE TABLE IF NOT EXISTS "NotificationPreferences" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_NotificationPreferences" PRIMARY KEY AUTOINCREMENT,
                "Key" TEXT NOT NULL,
                "EmailNotifications" INTEGER NOT NULL DEFAULT 0,
                "SmsNotifications" INTEGER NOT NULL DEFAULT 0,
                "InAppNotifications" INTEGER NOT NULL DEFAULT 0,
                "Orders" INTEGER NOT NULL DEFAULT 0,
                "Deliveries" INTEGER NOT NULL DEFAULT 0,
                "Payments" INTEGER NOT NULL DEFAULT 0,
                "SystemAlerts" INTEGER NOT NULL DEFAULT 0,
                "Promotions" INTEGER NOT NULL DEFAULT 0,
                "UpdatedAt" TEXT NOT NULL
            );
            """
            : """
            CREATE TABLE IF NOT EXISTS "NotificationPreferences" (
                "Id" integer NOT NULL GENERATED BY DEFAULT AS IDENTITY,
                CONSTRAINT "PK_NotificationPreferences" PRIMARY KEY ("Id"),
                "Key" text NOT NULL,
                "EmailNotifications" boolean NOT NULL DEFAULT false,
                "SmsNotifications" boolean NOT NULL DEFAULT false,
                "InAppNotifications" boolean NOT NULL DEFAULT false,
                "Orders" boolean NOT NULL DEFAULT false,
                "Deliveries" boolean NOT NULL DEFAULT false,
                "Payments" boolean NOT NULL DEFAULT false,
                "SystemAlerts" boolean NOT NULL DEFAULT false,
                "Promotions" boolean NOT NULL DEFAULT false,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            """;
        db.Database.ExecuteSqlRaw(prefsTable);
        db.Database.ExecuteSqlRaw(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_NotificationPreferences_Key" ON "NotificationPreferences" ("Key");""");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "DeviceToken registry DB unavailable — push lookups will fail until the db is fixed");
    }

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Enable CORS
    app.UseCors("AllowFrontend");

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseHttpsRedirection();
    app.MapControllers();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { 
        status = "healthy", 
        service = "NotificationService",
        timestamp = DateTime.UtcNow 
    }))
    .WithName("HealthCheck")
    .WithOpenApi();

    Log.Information("NotificationService started successfully on {Urls}", string.Join(", ", builder.WebHost.GetSetting("urls") ?? "http://localhost:5000"));
    
    app.Run();
}
catch (Exception ex)
{
    // Rethrow: AddApplicationDbContext's hard-fail (bad/missing DB config) must
    // kill the process with a non-zero exit, not be swallowed here into a
    // Fatal log line and an exit code of 0 — a restart-loop health gate would
    // see nothing actionable. See Common/DbProviderSelector.cs (issue #89).
    Log.Fatal(ex, "NotificationService failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
