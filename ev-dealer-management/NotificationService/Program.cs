using Serilog;
using Microsoft.EntityFrameworkCore;
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
    var dbConn = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(dbConn))
        dbConn = $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "notifications.db")}";
    builder.Services.AddDbContext<NotificationService.Data.NotificationDbContext>(options =>
        options.UseSqlite(dbConn));
    builder.Services.AddScoped<IDeviceTokenRegistry, DeviceTokenRegistry>();

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
            policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    // Add Controllers
    builder.Services.AddControllers();

    var app = builder.Build();

    // Create the registry schema on boot. Single-table with no history to
    // replay, so EnsureCreated is honest here (the other services ship
    // migrations and use Migrate()). Fail-soft on a locked/corrupt db file —
    // a dead registry must not take the 14 queue consumers down with it;
    // events then degrade to log-only, the pre-Issue-#33 behavior.
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationService.Data.NotificationDbContext>();
        db.Database.EnsureCreated();
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
    Log.Fatal(ex, "NotificationService failed to start");
}
finally
{
    Log.CloseAndFlush();
}
