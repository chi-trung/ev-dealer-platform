using Serilog;
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
    
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
    
    builder.Services.AddScoped<IVehicleService, VehicleService.Services.VehicleService>();
    
    // Vehicle event producer. Publishes to the shared "vehicle_events" topic
    // exchange (see Events/EventNames.cs and docs/EVENTS.md).
    builder.Services.AddSingleton<VehicleService.Services.IMessageProducer, VehicleService.Services.RabbitMQProducerService>();
    
    builder.Services.AddHealthChecks();
    
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
    app.MapHealthChecks("/health");
    
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.EnsureCreated();
    }
    
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "VehicleService failed to start");
}
finally
{
    Log.CloseAndFlush();
}
