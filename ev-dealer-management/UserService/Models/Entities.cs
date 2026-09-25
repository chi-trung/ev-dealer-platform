using System.ComponentModel.DataAnnotations;

namespace UserService.Models;

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
