using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using AutoMapper; // Add this using statement

var builder = WebApplication.CreateBuilder(args);

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
        builder =>
        {
            builder.WithOrigins("http://localhost:5173") // Frontend URL
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

// Register DbContext with an absolute path
var contentRoot = builder.Environment.ContentRootPath;
var dbPath = Path.Combine(contentRoot, "customers.db");
builder.Services.AddDbContext<CustomerService.Data.CustomerDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Register Custom Services
builder.Services.AddScoped<CustomerService.Services.ICustomerService, CustomerService.Services.CustomerService>();
builder.Services.AddScoped<CustomerService.Services.ITestDriveService, CustomerService.Services.TestDriveService>(); // Register TestDriveService

// Register AutoMapper
builder.Services.AddAutoMapper(typeof(CustomerService.Profiles.MappingProfile).Assembly);


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

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
