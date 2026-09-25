using System.Text.Json.Serialization;

namespace UserService.DTOs;

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
