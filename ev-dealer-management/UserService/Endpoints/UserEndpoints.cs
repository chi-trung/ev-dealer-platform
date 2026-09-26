using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserService.Data;
using UserService.DTOs;
using UserService.Services;

namespace UserService.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
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

// Issue #92: internal endpoint, only ever called by ReportingService. Note it
// is NOT the data-sync fan-out: DataSynchronizationService has no user
// dependency. The real caller is ReportService.GetSalesByStaffAsync
// (ReportService.cs:411 and :422), which needs a name and email per
// salesperson id. Either way the response is every user row — username, email,
// role and DealerId — so it was readable by anyone who could reach the service,
// which on Render means anyone on the internet.
//
// The shared key is applied through a role requirement rather than a bare
// RequireAuthorization: the promoted principal carries only the InternalService
// role, so requiring THAT role admits the machine call and nothing else. A plain
// RequireAuthorization would also admit any signed-in user holding a valid JWT,
// including a low-privilege one, because the internal key is an alternative way
// in rather than a stricter one. Same endpoint, strictly smaller audience than
// "any logged-in user".
app.MapGet("/api/internal/users",
    [Microsoft.AspNetCore.Authorization.Authorize(
        Roles = Common.Auth.InternalServiceAuthMiddleware.InternalRole)]
    async (UserDbContext db) =>
{
    var users = await db.Users
        .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.Role, u.IsActive, u.DealerId, u.CreatedAt, u.UpdatedAt))
        .ToListAsync();
    return Results.Ok(users);
});

// Issue #150: provisions the login account a customer signs in with. Called by
// CustomerService over the internal key, with the admin's chosen password.
//
// WHY A NEW ENDPOINT AND NOT /api/admin/users
// That one is [Authorize(Roles = "Admin")], and InternalServiceAuthMiddleware
// REPLACES the principal rather than extending it (Common/Auth/InternalServiceAuth.cs:194,
// :260) — the promoted identity carries the InternalService role and nothing
// else. A machine call to /api/admin/users is therefore a 403, not a 403 to
// work around by loosening the existing endpoint. Widening that endpoint to
// accept both roles would also give the machine caller a route to RegisterRequest,
// whose Role field it can then set to Admin.
//
// Role-gated the same way as /api/internal/users above, for the same reason:
// a bare [Authorize] would also admit any signed-in user, and this endpoint
// creates accounts that can log in.
app.MapPost("/api/internal/customer-accounts",
    [Microsoft.AspNetCore.Authorization.Authorize(
        Roles = Common.Auth.InternalServiceAuthMiddleware.InternalRole)]
    async (CustomerAccountRequest req, IUserService userService) =>
{
    var result = await userService.ProvisionCustomerAccountAsync(req);
    // 409, not 400: the caller's retry after a Username/Email collision is a
    // different request, and 400 invites a retry loop where 409 does not.
    return result.Success
        ? Results.Ok(result)
        : Results.Conflict(result);
});


    }
}