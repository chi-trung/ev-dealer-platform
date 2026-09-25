using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using UserService.Data;
using UserService.DTOs;
using UserService.Models;

namespace UserService.Services;
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
