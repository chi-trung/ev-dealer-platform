using Serilog;
using Common.Data;
using Common.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using MailKit.Security;
using System.Linq;
using System.ComponentModel.DataAnnotations;

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
    
    app.MapPost("/api/auth/register", async (RegisterRequest req, IUserService userService) =>
    {
        var result = await userService.RegisterAsync(req);
        return result.Success ? Results.Created($"/api/users/{result.UserId}", result) : Results.BadRequest(result);
    });
    
    app.MapPost("/api/auth/login", async (LoginRequest req, IUserService userService) =>
    {
        var result = await userService.LoginAsync(req);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    
    app.MapPost("/api/auth/forgot-password", async ([FromBody] ForgotPasswordRequest req, IUserService userService) =>
    {
        var result = await userService.ForgotPasswordAsync(req);
        return Results.Ok(result);
    });
    
    app.MapPost("/api/auth/reset-password", async ([FromBody] ResetPasswordRequest req, IUserService userService) =>
    {
        var result = await userService.ResetPasswordAsync(req);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    
    // Issue #50: authenticated in-session password change (Settings page posts
    // {currentPassword, newPassword} with the login JWT attached by services/api.js).
    // Unlike reset-password, no email token is involved: the current password IS
    // the proof of ownership, and a wrong one is a plain 400 — the same shape the
    // login endpoint uses so the frontend toast wording stays consistent.
    app.MapPost("/api/auth/change-password", [Microsoft.AspNetCore.Authorization.Authorize] async (System.Security.Claims.ClaimsPrincipal user, ChangePasswordRequest req, IUserService userService) =>
    {
        var userIdClaim = user.FindFirst("id")?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();
    
        var result = await userService.ChangePasswordAsync(userId, req);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    
    app.MapGet("/api/users/me", [Microsoft.AspNetCore.Authorization.Authorize] async (System.Security.Claims.ClaimsPrincipal user, IUserService userService) =>
    {
        var userIdClaim = user.FindFirst("id")?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();
    
        var result = await userService.GetUserByIdAsync(userId);
        return result.Success ? Results.Ok(result.User) : Results.NotFound(result.Message);
    });
    
    // User management endpoints - Admin only
    app.MapGet("/api/users", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (IUserService userService) =>
    {
        var result = await userService.GetUsersAsync();
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    
    // New endpoint for Admin to create approved users
    app.MapPost("/api/admin/users", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (RegisterRequest req, IUserService userService) =>
    {
        var result = await userService.CreateApprovedUserAsync(req);
        return result.Success ? Results.Created($"/api/users/{result.UserId}", result) : Results.BadRequest(result);
    });
    
    app.MapGet("/api/users/{id:int}", [Microsoft.AspNetCore.Authorization.Authorize] async (int id, System.Security.Claims.ClaimsPrincipal user, IUserService userService) =>
    {
        var currentUserRole = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        var currentUserIdClaim = user.FindFirst("id")?.Value;
        if (!int.TryParse(currentUserIdClaim, out var currentUserId))
            return Results.Unauthorized();
    
        if (currentUserRole != "Admin" && currentUserId != id)
            return Results.Forbid();
    
        var result = await userService.GetUserByIdAsync(id);
        return result.Success ? Results.Ok(result) : Results.NotFound(result.Message);
    });
    
    app.MapPut("/api/users/{id:int}", [Microsoft.AspNetCore.Authorization.Authorize] async (int id, UpdateUserRequest request, System.Security.Claims.ClaimsPrincipal user, IUserService userService) =>
    {
        var currentUserRole = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        var currentUserIdClaim = user.FindFirst("id")?.Value;
        if (!int.TryParse(currentUserIdClaim, out var currentUserId))
            return Results.Unauthorized();
    
        var result = await userService.UpdateUserAsync(id, request, currentUserRole, currentUserId);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    
    app.MapDelete("/api/users/{id:int}", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (int id, System.Security.Claims.ClaimsPrincipal user, IUserService userService) =>
    {
        var currentUserRole = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
    
        var result = await userService.DeleteUserAsync(id, currentUserRole);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    
    app.MapPut("/api/users/{id:int}/role", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (int id, ChangeRoleRequest request, IUserService userService) =>
    {
        var result = await userService.ChangeUserRoleAsync(id, request);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    
    app.MapPut("/api/users/{id:int}/approve", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (int id, IUserService userService) =>
    {
        var result = await userService.ApproveUserAsync(id);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    
    // Dealer list. Issue #121: UserService no longer owns the Dealers table
    // (VehicleService does), so this proxies VehicleService's own endpoint
    // rather than reading a table it no longer maps. Keeps the frontend's
    // existing call path through the gateway working unchanged.
    app.MapGet("/api/dealers", async (DealerIdValidator validator, ILogger<Program> logger) =>
    {
        try
        {
            var dealers = await validator.GetDealersAsync();
            return Results.Ok(dealers);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch dealers from VehicleService");
            return Results.Problem("Dealer service unavailable", statusCode: 503);
        }
    });
    
    // Internal endpoint for ReportingService to get users (no auth required for internal service calls)
    app.MapGet("/api/internal/users", async (UserDbContext db) =>
    {
        var users = await db.Users
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.Role, u.IsActive, u.DealerId, u.CreatedAt, u.UpdatedAt))
            .ToListAsync();
        return Results.Ok(users);
    });
    
    
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

// DTOs and minimal implementations
public record RegisterRequest(string Username, string Email, string FullName, string Password, string Role, int? DealerId);
public record LoginRequest(string Username, string Password);
public record ForgotPasswordRequest([property: JsonPropertyName("email")] string Email);
public record ResetPasswordRequest([property: JsonPropertyName("token")] string Token, [property: JsonPropertyName("newPassword")] string NewPassword);
// Issue #50: the Settings "Đổi mật khẩu" form posts exactly these camelCase
// fields. Minimal-API [FromBody] uses Web defaults (case-insensitive), so the
// names bind from either casing; no JsonPropertyName needed.
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UserDto(int Id, string Username, string Email, string FullName, string Role, bool IsActive, int? DealerId, DateTime CreatedAt, DateTime UpdatedAt);
public record UpdateUserRequest(string Email, string FullName);
public record ChangeRoleRequest(string Role);

public record AuthResult(bool Success, string Message, string? Token = null, int? UserId = null, UserDto? User = null);
public record UserListResult(bool Success, string Message, IEnumerable<UserDto>? Users = null);
public record UserResult(bool Success, string Message, UserDto? User = null);
public record PasswordResetResult(bool Success, string Message);

// EF Core DbContext and entities
public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options) { }
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    // NO DbSet<Dealer>: VehicleService owns the Dealers table (issue #121).
    // See the comment on the Dealer class -- two contexts with different
    // creation strategies (Migrate vs EnsureCreated) cannot share it.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(eb =>
        {
            eb.HasKey(u => u.Id);
            eb.HasIndex(u => u.Username).IsUnique();
            eb.HasIndex(u => u.Email);
            // No HasOne<Dealer>() FK: that would emit a constraint against a
            // table this context no longer owns. DealerId is validated in
            // application code (DealerIdValidator) instead.
        });

        modelBuilder.Entity<PasswordResetToken>(eb =>
        {
            eb.HasKey(t => t.Id);
            eb.HasIndex(t => t.Token);
            eb.HasIndex(t => t.UserId);
            eb.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Role { get; set; } = "DealerStaff";
    public bool IsActive { get; set; } = false; // Default to false, requires admin approval
    public int? DealerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// The `Dealers` table is OWNED BY VehicleService (issue #121). UserService used
// to emit it too, which broke a shared database: Migrate() here and
// EnsureCreated() there both claim the whole database, so whichever service
// boots second finds the table already present and EnsureCreated silently
// creates NOTHING (probed: it returns false rather than throwing, so /health
// stays green while every endpoint dies on "no such table").
//
// UserService does not write dealers; it only needs to (a) expose a list
// endpoint and (b) validate DealerId on registration. Both work over HTTP
// against VehicleService's own /api/dealers, so the table stays out of this
// context. The FK Users.DealerId -> Dealers.Id is therefore NOT a real DB
// constraint here; it is validated in code by DealerIdValidator below.
public class Dealer
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Region { get; set; } = string.Empty;

    // VehicleService owns the full 7-column shape (Contact, Email, Address,
    // CreatedAt, UpdatedAt). UserService no longer maps this to a table -- see
    // the comment above -- so these extra members exist purely so a Dealer
    // fetched from VehicleService's /api/dealers deserializes losslessly.
    // The field set below mirrors VehicleService/DTOs/DealerDto.cs EXACTLY,
    // including VehicleCount: that property is not a column (the controller
    // computes it from the Vehicles navigation), but it IS on the wire, and a
    // missing member here meant /api/dealers silently dropped it for every
    // caller of this proxy (review round 2, M3).
    [Required]
    [StringLength(20)]
    public string Contact { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Address { get; set; } = string.Empty;

    // Computed by VehicleService's controller, not stored. Kept here only to
    // preserve it across the proxy so the frontend's dealer list is complete.
    public int VehicleCount { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
}

public interface IUserService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request);
    Task<AuthResult> CreateApprovedUserAsync(RegisterRequest request); // New method for admin
    Task<AuthResult> LoginAsync(LoginRequest request);
    Task<UserListResult> GetUsersAsync();
    Task<UserResult> GetUserByIdAsync(int id);
    Task<UserResult> UpdateUserAsync(int id, UpdateUserRequest request, string currentUserRole, int currentUserId);
    Task<UserResult> DeleteUserAsync(int id, string currentUserRole);
    Task<UserResult> ChangeUserRoleAsync(int id, ChangeRoleRequest request);
    Task<UserResult> ApproveUserAsync(int id);
    Task<PasswordResetResult> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task<PasswordResetResult> ResetPasswordAsync(ResetPasswordRequest request);
    // Issue #50: authenticated password change for the Settings page.
    Task<PasswordResetResult> ChangePasswordAsync(int userId, ChangePasswordRequest request);
}

// Issue #121: validates that a DealerId refers to a real dealer without
// mapping the Dealers table in this context (VehicleService owns it now).
// Falls open on failure: an unreachable VehicleService must not block user
// registration entirely -- it returns true so the request proceeds, matching
// the pre-#121 behaviour where a missing table row was the only guard.
// Callers that need a hard guarantee can check the returned list instead.
public class DealerIdValidator
{
    private readonly HttpClient _http;
    private readonly ILogger<DealerIdValidator> _logger;
    // Short-lived cache: a signup should not cost a network round-trip every
    // time, but dealers are admin-managed so a long TTL would go stale.
    // This component is a SINGLETON (AddSingleton + AddHttpClient<T>), so the
    // cache fields are hit by every concurrent registration in the process.
    // A plain List<> reference + a separately-written DateTime is not atomic:
    // two threads can race the null/TTL check and stampede the endpoint, and
    // a reader can see _cacheAt advanced past a _cache that is still the old
    // list (torn read). The pair is stored and read atomically instead.
    private volatile Tuple<List<Dealer>, DateTime>? _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    public DealerIdValidator(HttpClient http, ILogger<DealerIdValidator> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<Dealer>> GetDealersAsync()
    {
        // BaseAddress is required: the request below is a RELATIVE URI, so
        // without it GetFromJsonAsync throws before any network call and the
        // caller sees an empty dealer list. Fail loudly here -- the proxy's
        // own /api/dealers endpoint catches and 503s -- rather than returning
        // [] and having every DealerId validation silently reject.
        var baseUri = _http.BaseAddress?.ToString().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUri))
            throw new InvalidOperationException(
                "Services:VehicleService is not configured: HttpClient BaseAddress is empty, " +
                "so the relative /api/dealers request cannot be built. Set Services__VehicleService " +
                "(see render.yaml / docker-compose.yml).");

        var dealers = await _http.GetFromJsonAsync<List<Dealer>>("/api/dealers")
            ?? new List<Dealer>();
        // Write the pair atomically: a concurrent IsValidAsync reader either
        // gets the old snapshot or this one, never a mismatched combination.
        _cache = Tuple.Create(dealers, DateTime.UtcNow);
        return dealers;
    }

    public async Task<bool> IsValidAsync(int dealerId)
    {
        var dealers = await GetCachedOrFreshAsync();
        return dealers.Any(d => d.Id == dealerId);
    }

    private async Task<List<Dealer>> GetCachedOrFreshAsync()
    {
        // Single atomic read of the cached pair.
        var snapshot = _cache;
        if (snapshot is not null && DateTime.UtcNow - snapshot.Item2 < CacheTtl)
            return snapshot.Item1;
        try { return await GetDealersAsync(); }
        catch (Exception ex)
        {
            // Fail OPEN, with a log line: a broker/dealer-service outage must
            // not lock out registration. The FK no longer exists in this
            // context, so there is nothing else to keep the row consistent.
            _logger.LogWarning(ex, "Could not reach VehicleService to validate dealer ids; accepting DealerId {Id} unchecked", 0);
            return snapshot?.Item1 ?? new List<Dealer>();
        }
    }
}

public class UserServiceImpl : IUserService
{
    private readonly UserDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IEmailService _emailService;
    private readonly DealerIdValidator _dealerValidator;

    // dealerValidator is optional: only DealerId validation needs it, and
    // making it nullable keeps callers that never touch registration (and the
    // test suite's LoginAsync pins) free of the HttpClient wiring.
    public UserServiceImpl(UserDbContext db, IConfiguration cfg, IEmailService emailService, DealerIdValidator? dealerValidator = null)
    {
        _db = db;
        _cfg = cfg;
        _emailService = emailService;
        _dealerValidator = dealerValidator;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Role))
            return new AuthResult(false, "All fields are required");

        var validRoles = new[] { "DealerStaff", "DealerManager", "EVMStaff" };
        if (!validRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
        {
            return new AuthResult(false, "Invalid role selected.");
        }

        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            return new AuthResult(false, "Username already exists");

        if (await _db.Users.AnyAsync(u => u.Email == request.Email))
            return new AuthResult(false, "Email already exists");

        // Issue #121: Dealers is owned by VehicleService, so the ID is
        // validated against its endpoint, not this context's (removed) table.
        // Issue #121: Dealers is owned by VehicleService now, so the id is
        // checked against its endpoint. _dealerValidator is null only in
        // callers that never pass a DealerId (e.g. the LoginAsync test pins);
        // a registration with no DealerId never reaches this call.
        if (request.DealerId.HasValue && _dealerValidator is not null
            && !await _dealerValidator.IsValidAsync(request.DealerId.Value))
            return new AuthResult(false, "Invalid Dealer ID");

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
            DealerId = request.DealerId,
            IsActive = false, // Account requires approval
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return new AuthResult(true, "User created successfully. Your account is pending approval.", UserId: user.Id);
    }

    public async Task<AuthResult> CreateApprovedUserAsync(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Role))
            return new AuthResult(false, "All fields are required");

        var validRoles = new[] { "DealerStaff", "DealerManager", "EVMStaff", "Admin" }; // Admin can create other Admins
        if (!validRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
        {
            return new AuthResult(false, "Invalid role selected.");
        }

        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            return new AuthResult(false, "Username already exists");

        if (await _db.Users.AnyAsync(u => u.Email == request.Email))
            return new AuthResult(false, "Email already exists");

        // Issue #121: Dealers is owned by VehicleService now, so the id is
        // checked against its endpoint. _dealerValidator is null only in
        // callers that never pass a DealerId (e.g. the LoginAsync test pins);
        // a registration with no DealerId never reaches this call.
        if (request.DealerId.HasValue && _dealerValidator is not null
            && !await _dealerValidator.IsValidAsync(request.DealerId.Value))
            return new AuthResult(false, "Invalid Dealer ID");

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
            DealerId = request.DealerId,
            IsActive = true, // Admin created users are active by default
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return new AuthResult(true, "User created and approved successfully.", UserId: user.Id);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == request.Username);
        if (user == null) return new AuthResult(false, "Tên đăng nhập hoặc mật khẩu không đúng.");

        if (!user.IsActive)
            return new AuthResult(false, "Tài khoản của bạn đang chờ phê duyệt từ quản trị viên.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return new AuthResult(false, "Tên đăng nhập hoặc mật khẩu không đúng.");

        // create token
        var jwt = _cfg.GetSection("Jwt");
        var key = jwt.GetValue<string>("Key") ?? "ReplaceThisWithASecretKeyForDevelopment";
        var issuer = jwt.GetValue<string>("Issuer") ?? "evm.local";
        var audience = jwt.GetValue<string>("Audience") ?? "evm.local";

        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var keyBytes = Encoding.UTF8.GetBytes(key);
        // Issue #36: staff accounts tied to a dealer carry a "dealer" claim so
        // the device-token registry lets them manage dealer:<n> subjects.
        // Omitted (not empty) when the account has no DealerId — consumers of
        // the claim parse int and fail closed.
        var claims = new List<System.Security.Claims.Claim> {
            new System.Security.Claims.Claim("id", user.Id.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.Username),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, user.Role)
        };
        if (user.DealerId.HasValue)
            claims.Add(new System.Security.Claims.Claim("dealer", user.DealerId.Value.ToString()));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(8),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(descriptor);
        var tokenString = tokenHandler.WriteToken(token);
        
        var userDto = new UserDto(user.Id, user.Username, user.Email, user.FullName, user.Role, user.IsActive, user.DealerId, user.CreatedAt, user.UpdatedAt);

        return new AuthResult(true, "Login successful", Token: tokenString, UserId: user.Id, User: userDto);
    }

    public async Task<UserListResult> GetUsersAsync()
    {
        var users = await _db.Users
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.Role, u.IsActive, u.DealerId, u.CreatedAt, u.UpdatedAt))
            .ToListAsync();

        return new UserListResult(true, "Users retrieved successfully", users);
    }

    public async Task<UserResult> GetUserByIdAsync(int id)
    {
        var user = await _db.Users
            .Where(u => u.Id == id)
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.Role, u.IsActive, u.DealerId, u.CreatedAt, u.UpdatedAt))
            .FirstOrDefaultAsync();

        if (user == null)
            return new UserResult(false, "User not found");

        return new UserResult(true, "User retrieved successfully", user);
    }

    public async Task<UserResult> UpdateUserAsync(int id, UpdateUserRequest request, string currentUserRole, int currentUserId)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FullName))
            return new UserResult(false, "Email and full name are required");

        if (currentUserRole != "Admin" && currentUserId != id)
            return new UserResult(false, "Unauthorized to update this user");

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return new UserResult(false, "User not found");

        if (await _db.Users.AnyAsync(u => u.Email == request.Email && u.Id != id))
            return new UserResult(false, "Email already exists");

        user.Email = request.Email;
        user.FullName = request.FullName;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userDto = new UserDto(user.Id, user.Username, user.Email, user.FullName, user.Role, user.IsActive, user.DealerId, user.CreatedAt, user.UpdatedAt);
        return new UserResult(true, "User updated successfully", userDto);
    }

    public async Task<UserResult> DeleteUserAsync(int id, string currentUserRole)
    {
        if (currentUserRole != "Admin")
            return new UserResult(false, "Only admins can delete users");

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return new UserResult(false, "User not found");

        _db.Users.Remove(user); // Hard delete for this example
        await _db.SaveChangesAsync();

        return new UserResult(true, "User deleted successfully");
    }

    public async Task<UserResult> ChangeUserRoleAsync(int id, ChangeRoleRequest request)
    {
        var validRoles = new[] { "DealerStaff", "DealerManager", "EVMStaff", "Admin" };
        if (!validRoles.Contains(request.Role))
            return new UserResult(false, "Invalid role");

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return new UserResult(false, "User not found");

        user.Role = request.Role;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userDto = new UserDto(user.Id, user.Username, user.Email, user.FullName, user.Role, user.IsActive, user.DealerId, user.CreatedAt, user.UpdatedAt);
        return new UserResult(true, "User role updated successfully", userDto);
    }

    public async Task<UserResult> ApproveUserAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return new UserResult(false, "User not found");

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        
        var userDto = new UserDto(user.Id, user.Username, user.Email, user.FullName, user.Role, user.IsActive, user.DealerId, user.CreatedAt, user.UpdatedAt);
        return new UserResult(true, "User approved successfully", userDto);
    }
    
    // ... (rest of the methods are the same)
    public async Task<PasswordResetResult> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        Console.WriteLine($"[DEBUG] ForgotPasswordAsync called with email: {request.Email}");

        if (string.IsNullOrWhiteSpace(request.Email))
            return new PasswordResetResult(false, "Email is required");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);

        Console.WriteLine($"[DEBUG] User found: {user != null}");

        // Always return success to prevent email enumeration attacks
        if (user == null)
        {
            Console.WriteLine("[DEBUG] User is null, returning early");
            return new PasswordResetResult(true, "If the email exists, a password reset link has been sent");
        }

        // Generate secure random token
        var tokenBytes = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        var token = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");

        Console.WriteLine($"[DEV-DEBUG] Generated Password Reset Token: {token}");

        // Invalidate any existing tokens for this user
        var existingTokens = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var t in existingTokens)
        {
            t.IsUsed = true;
            t.UsedAt = DateTime.UtcNow;
        }

        // Create new reset token (expires in 1 hour)
        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.PasswordResetTokens.Add(resetToken);
        await _db.SaveChangesAsync();

        // Send email with reset link
        var frontendUrl = _cfg.GetValue<string>("FrontendUrl") ?? "http://localhost:5173";
        var resetLink = $"{frontendUrl}/reset-password?token={token}";

        Console.WriteLine($"[DEBUG] About to send email to {user.Email}");
        Console.WriteLine($"[DEBUG] Reset link: {resetLink}");

        try
        {
            await _emailService.SendPasswordResetEmailAsync(user.Email, user.FullName, resetLink);
            Console.WriteLine("[DEBUG] Email service called successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Email service failed: {ex.Message}");
        }

        return new PasswordResetResult(true, "If the email exists, a password reset link has been sent");
    }

    public async Task<PasswordResetResult> ResetPasswordAsync(ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return new PasswordResetResult(false, "Token and new password are required");

        if (request.NewPassword.Length < 6)
            return new PasswordResetResult(false, "Password must be at least 6 characters");

        // Find valid token
        var resetToken = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);

        if (resetToken == null)
            return new PasswordResetResult(false, "Invalid or expired reset token");

        // Get user
        var user = await _db.Users.FindAsync(resetToken.UserId);
        if (user == null || !user.IsActive)
            return new PasswordResetResult(false, "User not found");

        // Update password
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        // Mark token as used
        resetToken.IsUsed = true;
        resetToken.UsedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return new PasswordResetResult(true, "Password has been reset successfully");
    }

    // Issue #50: in-session change from the Settings page. The current password
    // is the proof of ownership (this is NOT the token-based reset flow), so a
    // wrong one is a plain failure with no email-enumeration softening needed —
    // the caller already knows the account. Length floor and hashing follow
    // ResetPasswordAsync exactly so the two paths can't drift.
    public async Task<PasswordResetResult> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null || !user.IsActive)
            return new PasswordResetResult(false, "Không tìm thấy người dùng.");

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return new PasswordResetResult(false, "Cần mật khẩu hiện tại và mật khẩu mới.");

        if (request.NewPassword.Length < 6)
            return new PasswordResetResult(false, "Mật khẩu mới phải có ít nhất 6 ký tự.");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return new PasswordResetResult(false, "Mật khẩu hiện tại không đúng.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        // A live reset token survives this change — and resetting with it needs
        // only the token. If the user just proved ownership of the password,
        // any outstanding forgot-password link they abandoned must die too,
        // or "change my password" silently leaves the old leak path open for
        // its remaining hour.
        var liveTokens = await _db.PasswordResetTokens
            .Where(t => t.UserId == userId && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var t in liveTokens)
        {
            t.IsUsed = true;
            t.UsedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        return new PasswordResetResult(true, "Đổi mật khẩu thành công.");
    }
}

// Email Service Interface and Implementation
public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration cfg, ILogger<EmailService> logger)
    {
        _cfg = cfg;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink)
    {
        var emailSettings = _cfg.GetSection("EmailSettings");
        var smtpHost = emailSettings.GetValue<string>("SmtpHost");
        var smtpPort = emailSettings.GetValue<int>("SmtpPort");
        var smtpUser = emailSettings.GetValue<string>("SmtpUser");
        var smtpPassword = emailSettings.GetValue<string>("SmtpPassword");
        var fromEmail = emailSettings.GetValue<string>("FromEmail") ?? smtpUser;
        var fromName = emailSettings.GetValue<string>("FromName") ?? "EV Dealer Management";
        var enableSsl = emailSettings.GetValue<bool>("EnableSsl", true);

        // If SMTP not configured, log instead of sending
        if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUser))
        {
            Console.WriteLine("\n=== PASSWORD RESET EMAIL ===");
            Console.WriteLine($"To: {toEmail}");
            Console.WriteLine($"Subject: Password Reset Request");
            Console.WriteLine($"Reset Link: {resetLink}");
            Console.WriteLine("============================\n");
            _logger.LogInformation("SMTP not configured. Password reset link logged to console for {Email}", toEmail);
            return;
        }

        try
        {
            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(smtpUser, smtpPassword);

            var message = new MimeKit.MimeMessage();
            message.From.Add(new MimeKit.MailboxAddress(fromName, fromEmail));
            message.To.Add(new MimeKit.MailboxAddress(userName, toEmail));
            message.Subject = "Reset Your Password - EV Dealer Management";

            var bodyBuilder = new MimeKit.BodyBuilder
            {
                HtmlBody = $@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <style>
                            body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                            .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                            .header {{ background-color: #4CAF50; color: white; padding: 20px; text-align: center; }}
                            .content {{ background-color: #f9f9f9; padding: 30px; border-radius: 5px; margin-top: 20px; }}
                            .button {{ display: inline-block; padding: 12px 30px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
                            .footer {{ text-align: center; margin-top: 30px; font-size: 12px; color: #666; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <div class='header'>
                                <h1>Password Reset Request</h1>
                            </div>
                            <div class='content'>
                                <p>Hello {userName},</p>
                                <p>We received a request to reset your password for your EV Dealer Management account.</p>
                                <p>Click the button below to reset your password:</p>
                                <p style='text-align: center;'>
                                    <a href='{resetLink}' class='button'>Reset Password</a>
                                </p>
                                <p>Or copy and paste this link into your browser:</p>
                                <p style='word-break: break-all; color: #666;'>{resetLink}</p>
                                <p><strong>This link will expire in 1 hour.</strong></p>
                                <p>If you didn't request a password reset, please ignore this email or contact support if you have concerns.</p>
                            </div>
                            <div class='footer'>
                                <p>&copy; 2024 EV Dealer Management System. All rights reserved.</p>
                            </div>
                        </div>
                    </body>
                    </html>
                ",
                TextBody = $@"
Hello {userName},

We received a request to reset your password for your EV Dealer Management account.

Click the link below to reset your password:
{resetLink}

This link will expire in 1 hour.

If you didn't request a password reset, please ignore this email or contact support if you have concerns.

© 2024 EV Dealer Management System. All rights reserved.
                "
            };

            message.Body = bodyBuilder.ToMessageBody();

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Password reset email sent successfully to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            throw new Exception("Failed to send email. Please try again later.");
        }
    }
}
