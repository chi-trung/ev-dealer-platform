using Serilog;
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
