using Serilog;
using Common.Data;
using SalesService.Data;
using SalesService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
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
            policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
                  .AllowAnyMethod()
                  .AllowAnyHeader();
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
    builder.Services.AddHealthChecks();
    
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
    
    app.MapControllers();
    
    // Liveness probe for the API gateway aggregate /health (docs/GATEWAY.md).
    app.MapHealthChecks("/health");
    
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SalesService failed to start");
}
finally
{
    Log.CloseAndFlush();
}
