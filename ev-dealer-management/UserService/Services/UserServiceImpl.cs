using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using UserService.Data;
using UserService.DTOs;
using UserService.Models;

namespace UserService.Services;
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
