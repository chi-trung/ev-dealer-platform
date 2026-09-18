using Serilog;
using Common.Data;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using AutoMapper; // Add this using statement

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
    Log.Information("Starting CustomerService...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog(); // Issue #63: route all ILogger<T> through the static Serilog logger above
    
    // Add services to the container.
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks();
    
    builder.Services.AddControllers();
    
    // Add CORS policy
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowSpecificOrigin",
            policy =>
            {
                // Origins come from config as one comma-separated string
                // (Cors__AllowedOrigins="https://a,https://b") so a deployed
                // frontend (e.g. the Vercel app) can be permitted without a
                // rebuild. Default keeps the Vite dev port working unchanged.
                // Origins must be exact "scheme://host[:port]" values: no
                // trailing slash and no wildcard patterns (WithOrigins stores
                // them verbatim, so e.g. "https://*.vercel.app" silently never
                // matches). Same pattern as APIGatewayService (Issue #77).
                var defaultOrigins = new[]
                {
                    "http://localhost:5173",
                    "http://localhost:5174",
                };
                var configured = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var origins = configured.Length > 0 ? configured : defaultOrigins;
                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .WithExposedHeaders("Location"); // Expose the Location header for 201 Created responses
            });
    });
    
    // Add JWT Authentication
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
    
    // Add Authorization
    builder.Services.AddAuthorization();
    
    // Register DbContext with an absolute path. A configured connection string
    // (env override ConnectionStrings__DefaultConnection) wins so docker-compose can
    // relocate the DB onto the /app/data volume; with none set, behavior is
    // unchanged from before (ContentRootPath/customers.db, i.e. the project dir
    // under `dotnet run`).
    // Issue #89: provider switch centralised in Common.DbProviderSelector.
    // The fallback keeps the pre-existing absolute ContentRootPath/customers.db
    // path (not just "customers.db") so `dotnet run` behaviour is unchanged.
    builder.Services.AddApplicationDbContext<CustomerService.Data.CustomerDbContext>(
        builder.Configuration,
        sqliteFallback: $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "customers.db")}");
    
    // Register Custom Services
    builder.Services.AddScoped<CustomerService.Services.ICustomerService, CustomerService.Services.CustomerService>();
    builder.Services.AddScoped<CustomerService.Services.ITestDriveService, CustomerService.Services.TestDriveService>(); // Register TestDriveService
    
    // Register AutoMapper (v15+: DI built into the core package; the archived
    // AutoMapper.Extensions.Microsoft.DependencyInjection shim is gone)
    builder.Services.AddAutoMapper(cfg => cfg.AddMaps(typeof(CustomerService.Profiles.MappingProfile).Assembly));
    
    
    // Register RabbitMQ Producer Service
    builder.Services.AddSingleton<CustomerService.Services.IMessageProducer, CustomerService.Services.RabbitMQProducerService>();
    
    // Register Background Service for consuming VehicleReservedEvent
    builder.Services.AddHostedService<CustomerService.Consumers.VehicleReservedEventConsumer>();
    
    var app = builder.Build();
    
    // Automatically apply migrations on startup (for development)
    if (app.Environment.IsDevelopment())
    {
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CustomerService.Data.CustomerDbContext>();
            dbContext.Database.Migrate();
        }
    }
    
    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    
    // app.UseHttpsRedirection();
    
    // Use CORS policy
    app.UseCors("AllowSpecificOrigin");
    
    // Enable Authentication and Authorization
    app.UseAuthentication();
    app.UseAuthorization();
    
    app.MapControllers();
    
    // Liveness probe for the API gateway aggregate /health (docs/GATEWAY.md).
    app.MapHealthChecks("/health");
    
    var summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };
    
    app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
            new WeatherForecast
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                summaries[Random.Shared.Next(summaries.Length)]
            ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();
    
    app.Run();
}
catch (Exception ex)
{
    // Rethrow: AddApplicationDbContext's hard-fail (bad/missing DB config) must
    // kill the process with a non-zero exit, not be swallowed here into a
    // Fatal log line and an exit code of 0 — a restart-loop health gate would
    // see nothing actionable. See Common/DbProviderSelector.cs (issue #89).
    Log.Fatal(ex, "CustomerService failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
