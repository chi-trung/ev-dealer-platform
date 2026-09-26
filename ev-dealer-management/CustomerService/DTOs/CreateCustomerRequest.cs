using System.ComponentModel.DataAnnotations;

namespace CustomerService.DTOs;

public class CreateCustomerRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone]
    public string? Phone { get; set; }

    public string? Address { get; set; }

    [Required]
    public int DealerId { get; set; }

    /// <summary>
    /// Password for the login account created alongside this customer
    /// (issue #150). Chosen by the admin, who then hands it to the customer out
    /// of band.
    /// </summary>
    /// <remarks>
    /// WHY THE ADMIN TYPES IT RATHER THAN THE SYSTEM GENERATING ONE
    /// There is no working outbound email on this deployment — Render's free
    /// tier blocks SMTP 25/465/587, and the HTTP email provider in plan P6 is
    /// not chosen yet. The only delivery channel that exists today is
    /// forgot-password, which needs that email. So a generated password would
    /// have nowhere to go, and a one-time "copy this from the response" field
    /// would need a force-password-change column that Users does not have.
    /// Admin-typed works today with no new table.
    ///
    /// The cost is real and worth stating: the admin chooses another person's
    /// password, and nothing here can force a change later. Users has no
    /// ForcePasswordChange flag, so a customer may keep this password forever.
    /// Adding one is a separate change.
    ///
    /// This value crosses browser → gateway → CustomerService → UserService.
    /// It is never logged and never returned in any response DTO. If a future
    /// change adds request-body logging to either service, this field becomes
    /// a plaintext credential in the logs — check that first.
    /// </remarks>
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    public string? Status { get; set; } // e.g., "active", "inactive", "pending"

    // Optional: Add DateOfBirth if needed based on detailed requirements
    // public DateTime? DateOfBirth { get; set; }
}
