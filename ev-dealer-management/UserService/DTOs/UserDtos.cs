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

/// <summary>
/// Body for the internal customer-account provisioning call (issue #150).
/// Deliberately NOT <see cref="RegisterRequest"/>: that record carries a Role
/// the caller chooses, and the whole point of this endpoint is that the role is
/// fixed to Customer by this service. See the endpoint for the reasoning.
/// </summary>
public record CustomerAccountRequest(string Username, string Email, string FullName, string Password, int? DealerId);

/// <summary>
/// Result of provisioning a customer account. UserId is the value
/// CustomerService writes into Customers.UserId; nothing else is returned
/// because nothing else is needed, and PasswordHash in particular must not
/// leave this service.
/// </summary>
public record CustomerAccountResult(bool Success, string Message, int? UserId = null);

public record AuthResult(bool Success, string Message, string? Token = null, int? UserId = null, UserDto? User = null);
public record UserListResult(bool Success, string Message, IEnumerable<UserDto>? Users = null);
public record UserResult(bool Success, string Message, UserDto? User = null);
public record PasswordResetResult(bool Success, string Message);
