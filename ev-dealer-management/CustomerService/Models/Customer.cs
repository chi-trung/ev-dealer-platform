using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CustomerService.Models;

public class Customer
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone]
    public string? Phone { get; set; }

    public string? Address { get; set; }

    [Required] // Assuming DealerId is always required based on the migration
    public int DealerId { get; set; }

    public string? Status { get; set; } // e.g., "active", "inactive", "pending"

    public DateTime JoinDate { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; } // Track the last update time

    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public ICollection<TestDrive> TestDrives { get; set; } = new List<TestDrive>();
    public ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();
}

public class Purchase
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Vehicle { get; set; } = string.Empty;
    // Same precision reason as the other money columns: a scale-less numeric
    // under Postgres drops the cents half of the amount.
    [Column(TypeName = "decimal(18, 2)")]
    public decimal Amount { get; set; }
    public DateTime PurchaseDate { get; set; }
}
