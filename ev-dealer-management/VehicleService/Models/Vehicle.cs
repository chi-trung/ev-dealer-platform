using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VehicleService.Models;

public class Vehicle
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Model { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [Range(0, double.MaxValue)]
    // Precision is required under Postgres: an untyped decimal maps to
    // `numeric` with no scale, and Npgsql's default scale rounds cents away
    // (45000.75 -> 45001) because the provider's built-in scale is 0 for a
    // store type without one. SQLite stores TEXT and is unaffected either way.
    [Column(TypeName = "decimal(18, 2)")]
    public decimal Price { get; set; }

    [Required]
    [Range(0, double.MaxValue)]
    public double BatteryCapacity { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int Range { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    // Navigation properties
    public int DealerId { get; set; }
    public Dealer? Dealer { get; set; }

    // Collections
    public ICollection<VehicleImage> Images { get; set; } = new List<VehicleImage>();
    public ICollection<ColorVariant> ColorVariants { get; set; } = new List<ColorVariant>();
    public VehicleSpecifications? Specifications { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class VehicleImage
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(500)]
    public string Url { get; set; } = string.Empty;

    [StringLength(100)]
    public string? AltText { get; set; }

    public int Order { get; set; }

    // Foreign key
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
}

public class ColorVariant
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(7)] // Hex color format
    public string Hex { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }

    // Foreign key
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
}

public class VehicleSpecifications
{
    [Key]
    public int Id { get; set; }

    [StringLength(100)]
    public string? Acceleration { get; set; }

    [StringLength(100)]
    public string? TopSpeed { get; set; }

    [StringLength(100)]
    public string? Charging { get; set; }

    [StringLength(100)]
    public string? Warranty { get; set; }

    [Range(1, 20)]
    public int? Seats { get; set; }

    [StringLength(100)]
    public string? Cargo { get; set; }

    // Foreign key
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
}
