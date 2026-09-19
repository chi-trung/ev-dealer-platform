using Serilog;
using Common.Data;
using Common.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using VehicleService.Data;
using VehicleService.Services;

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
    Log.Information("Starting VehicleService...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog(); // Issue #63: route all ILogger<T> through the static Serilog logger above
    
    // Add services to the container.
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    
    builder.Services.AddControllers();
    
    // Issue #89: provider switch centralised in Common.DbProviderSelector.
    builder.Services.AddApplicationDbContext<ApplicationDbContext>(
        builder.Configuration,
        sqliteFallback: "Data Source=vehicles.db");
    
    builder.Services.AddScoped<IVehicleService, VehicleService.Services.VehicleService>();
    
    // Vehicle event producer. Publishes to the shared "vehicle_events" topic
    // exchange (see Events/EventNames.cs and docs/EVENTS.md).
    builder.Services.AddSingleton<VehicleService.Services.IMessageProducer, VehicleService.Services.RabbitMQProducerService>();
    
    builder.Services.AddHealthChecks()
        // #135: an empty AddHealthChecks() answers 200 unconditionally — even
        // for this service's publisher, whose InitializeRabbitMQ catches and
        // logs a failure without rethrowing, leaving every publish a silent
        // no-op. The broker probe answers connectivity independently of that
        // lazy singleton (see the notes in Common.Health).
        .AddBrokerProbeCheck()
        .AddDatabaseCheck<VehicleService.Data.ApplicationDbContext>();

    // REMOVED: Add CORS services
    // builder.Services.AddCors(options =>
    // {
    //     options.AddPolicy("AllowFrontend", policy => // Changed policy name to be more generic
    //     {
    //         policy.WithOrigins("http://localhost:5173", "http://localhost:5174") // Allow both frontend and potentially VehicleService itself
    //               .AllowAnyMethod()
    //               .AllowAnyHeader()
    //               .AllowCredentials();
    //     });
    // });
    
    var app = builder.Build();
    
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    
    // REMOVED: Use CORS policy
    // app.UseCors("AllowFrontend");
    app.UseHttpsRedirection();
    app.UseStaticFiles();
    
    app.MapControllers();
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = BrokerHealthCheckExtensions.WriteHealthReportAsync
    });
    
    using (var scope = app.Services.CreateScope())
    {
        // Issue #121: Migrate(), not EnsureCreated(). Both this service and
        // UserService point at one database, and the two creation strategies
        // cannot co-own it. EnsureCreated() gates on the WHOLE database
        // existing: on a DB the other service already made it creates NOTHING
        // (returns false, does not throw, so /health stays green while every
        // endpoint dies on "no such table"), and it never writes
        // __EFMigrationsHistory, so UserService.Migrate() could not see the
        // work and replayed its own history into a crash. Both boot orderings
        // were broken (probed). Migrate() on both sides with disjoint table
        // sets is the fix; the Baseline migration carries the seed data that
        // EnsureCreated used to apply via HasData.
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.Migrate();
    }
    
    app.Run();
}
catch (Exception ex)
{
    // Report startup failure with a non-zero process status, not just a log.
    // Docker's unless-stopped policy may restart either exit status; this
    // preserves the failure signal for callers and exit-code-based policies.
    // The finally block still flushes Serilog before the exception escapes.
    Log.Fatal(ex, "VehicleService failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
