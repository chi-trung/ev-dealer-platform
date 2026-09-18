using Microsoft.EntityFrameworkCore;
using VehicleService.Models;

namespace VehicleService.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Vehicle> Vehicles { get; set; }
    public DbSet<Dealer> Dealers { get; set; }
    public DbSet<VehicleType> VehicleTypes { get; set; }
    public DbSet<VehicleImage> VehicleImages { get; set; }
    public DbSet<ColorVariant> ColorVariants { get; set; }
    public DbSet<VehicleSpecifications> VehicleSpecifications { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure relationships
        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.Dealer)
            .WithMany(d => d.Vehicles)
            .HasForeignKey(v => v.DealerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.Specifications)
            .WithOne(s => s.Vehicle)
            .HasForeignKey<VehicleSpecifications>(s => s.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VehicleImage>()
            .HasOne(vi => vi.Vehicle)
            .WithMany(v => v.Images)
            .HasForeignKey(vi => vi.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ColorVariant>()
            .HasOne(cv => cv.Vehicle)
            .WithMany(v => v.ColorVariants)
            .HasForeignKey(cv => cv.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed data
        SeedData(modelBuilder);
    }

    // Seed timestamps are pinned to a constant, not DateTime.UtcNow. HasData
    // values become part of the model, so a UtcNow seed makes the snapshot
    // non-reproducible and every future migration emit UpdateData rows that
    // rewrite these timestamps to whatever instant it was scaffolded at
    // (Issue #97). Pinned values keep existing DBs untouched too.
    private static readonly DateTime SeedTimestamp =
        new(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

    private void SeedData(ModelBuilder modelBuilder)
    {
        // Seed VehicleTypes
        modelBuilder.Entity<VehicleType>().HasData(
            new VehicleType { Id = 1, Value = "sedan", Label = "Sedan" },
            new VehicleType { Id = 2, Value = "suv", Label = "SUV" },
            new VehicleType { Id = 3, Value = "hatchback", Label = "Hatchback" },
            new VehicleType { Id = 4, Value = "coupe", Label = "Coupe" },
            new VehicleType { Id = 5, Value = "convertible", Label = "Convertible" },
            new VehicleType { Id = 6, Value = "truck", Label = "Truck" }
        );

        // Seed Dealers
        modelBuilder.Entity<Dealer>().HasData(
            new Dealer
            {
                Id = 1,
                Name = "Tesla Center HCMC",
                Region = "Ho Chi Minh City",
                Contact = "0901234567",
                Email = "hcmc@tesla.com",
                Address = "123 Nguyen Hue, District 1, HCMC",
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Dealer
            {
                Id = 2,
                Name = "BMW Center District 1",
                Region = "Ho Chi Minh City",
                Contact = "0902345678",
                Email = "district1@bmw.com",
                Address = "456 Le Loi, District 1, HCMC",
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Dealer
            {
                Id = 3,
                Name = "Audi Center District 2",
                Region = "Ho Chi Minh City",
                Contact = "0903456789",
                Email = "district2@audi.com",
                Address = "789 Dong Khoi, District 2, HCMC",
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Dealer
            {
                Id = 4,
                Name = "Mercedes-Benz Center District 3",
                Region = "Ho Chi Minh City",
                Contact = "0904567890",
                Email = "district3@mercedes.com",
                Address = "321 Nguyen Van Cu, District 3, HCMC",
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            }
        );

        // Seed Vehicles
        modelBuilder.Entity<Vehicle>().HasData(
            new Vehicle
            {
                Id = 1,
                Model = "Tesla Model 3",
                Type = "sedan",
                Price = 45000,
                BatteryCapacity = 75,
                Range = 350,
                StockQuantity = 12,
                Description = "Premium electric sedan with autopilot capabilities",
                DealerId = 1,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Vehicle
            {
                Id = 2,
                Model = "Tesla Model Y",
                Type = "suv",
                Price = 55000,
                BatteryCapacity = 75,
                Range = 330,
                StockQuantity = 8,
                Description = "Versatile electric SUV perfect for families",
                DealerId = 1,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Vehicle
            {
                Id = 3,
                Model = "BMW i4",
                Type = "sedan",
                Price = 52000,
                BatteryCapacity = 83.9,
                Range = 300,
                StockQuantity = 6,
                Description = "Luxury electric sedan with BMW's signature driving dynamics",
                DealerId = 2,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Vehicle
            {
                Id = 4,
                Model = "Audi e-tron",
                Type = "suv",
                Price = 65000,
                BatteryCapacity = 95,
                Range = 222,
                StockQuantity = 4,
                Description = "Premium electric SUV with quattro all-wheel drive",
                DealerId = 3,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Vehicle
            {
                Id = 5,
                Model = "Mercedes EQS",
                Type = "sedan",
                Price = 120000,
                BatteryCapacity = 107.8,
                Range = 350,
                StockQuantity = 2,
                Description = "Ultra-luxury electric sedan with cutting-edge technology",
                DealerId = 4,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            }
        );

        // Seed Vehicle Specifications
        modelBuilder.Entity<VehicleSpecifications>().HasData(
            new VehicleSpecifications
            {
                Id = 1,
                VehicleId = 1,
                Acceleration = "3.1s 0-60mph",
                TopSpeed = "162 mph",
                Charging = "250 kW Supercharging",
                Warranty = "4 years/50,000 miles",
                Seats = 5,
                Cargo = "15 cu ft"
            },
            new VehicleSpecifications
            {
                Id = 2,
                VehicleId = 2,
                Acceleration = "3.5s 0-60mph",
                TopSpeed = "155 mph",
                Charging = "250 kW Supercharging",
                Warranty = "4 years/50,000 miles",
                Seats = 7,
                Cargo = "76 cu ft"
            },
            new VehicleSpecifications
            {
                Id = 3,
                VehicleId = 3,
                Acceleration = "3.9s 0-60mph",
                TopSpeed = "118 mph",
                Charging = "150 kW DC Fast Charging",
                Warranty = "4 years/50,000 miles",
                Seats = 5,
                Cargo = "16 cu ft"
            },
            new VehicleSpecifications
            {
                Id = 4,
                VehicleId = 4,
                Acceleration = "5.5s 0-60mph",
                TopSpeed = "124 mph",
                Charging = "150 kW DC Fast Charging",
                Warranty = "4 years/50,000 miles",
                Seats = 5,
                Cargo = "27.2 cu ft"
            },
            new VehicleSpecifications
            {
                Id = 5,
                VehicleId = 5,
                Acceleration = "4.3s 0-60mph",
                TopSpeed = "130 mph",
                Charging = "200 kW DC Fast Charging",
                Warranty = "4 years/50,000 miles",
                Seats = 5,
                Cargo = "22 cu ft"
            }
        );

        // Seed Vehicle Images (Issue #83: /images/* is served from
        // wwwroot/images by UseStaticFiles + the gateway /images route — NOT
        // /src/assets/*, which matches the frontend SPA rewrite and 404s/returns HTML)
        modelBuilder.Entity<VehicleImage>().HasData(
            new VehicleImage { Id = 1, VehicleId = 1, Url = "/images/seed-car1.webp", AltText = "Tesla Model 3 Front", Order = 1 },
            new VehicleImage { Id = 2, VehicleId = 1, Url = "/images/seed-car2.webp", AltText = "Tesla Model 3 Side", Order = 2 },
            new VehicleImage { Id = 3, VehicleId = 1, Url = "/images/seed-car3.webp", AltText = "Tesla Model 3 Interior", Order = 3 },
            new VehicleImage { Id = 4, VehicleId = 2, Url = "/images/seed-car2.webp", AltText = "Tesla Model Y Front", Order = 1 },
            new VehicleImage { Id = 5, VehicleId = 2, Url = "/images/seed-car3.webp", AltText = "Tesla Model Y Side", Order = 2 },
            new VehicleImage { Id = 6, VehicleId = 2, Url = "/images/seed-car4.webp", AltText = "Tesla Model Y Interior", Order = 3 },
            new VehicleImage { Id = 7, VehicleId = 3, Url = "/images/seed-car3.webp", AltText = "BMW i4 Front", Order = 1 },
            new VehicleImage { Id = 8, VehicleId = 3, Url = "/images/seed-car4.webp", AltText = "BMW i4 Side", Order = 2 },
            new VehicleImage { Id = 9, VehicleId = 4, Url = "/images/seed-car4.webp", AltText = "Audi e-tron Front", Order = 1 },
            new VehicleImage { Id = 10, VehicleId = 5, Url = "/images/seed-car1.webp", AltText = "Mercedes EQS Front", Order = 1 },
            new VehicleImage { Id = 11, VehicleId = 5, Url = "/images/seed-car2.webp", AltText = "Mercedes EQS Side", Order = 2 }
        );

        // Seed Color Variants
        modelBuilder.Entity<ColorVariant>().HasData(
            new ColorVariant { Id = 1, VehicleId = 1, Name = "Pearl White", Hex = "#FFFFFF", Stock = 5 },
            new ColorVariant { Id = 2, VehicleId = 1, Name = "Midnight Silver", Hex = "#2C2C2C", Stock = 4 },
            new ColorVariant { Id = 3, VehicleId = 1, Name = "Deep Blue", Hex = "#1E3A8A", Stock = 3 },
            new ColorVariant { Id = 4, VehicleId = 2, Name = "Pearl White", Hex = "#FFFFFF", Stock = 3 },
            new ColorVariant { Id = 5, VehicleId = 2, Name = "Midnight Silver", Hex = "#2C2C2C", Stock = 3 },
            new ColorVariant { Id = 6, VehicleId = 2, Name = "Red Multi-Coat", Hex = "#DC2626", Stock = 2 },
            new ColorVariant { Id = 7, VehicleId = 3, Name = "Alpine White", Hex = "#FFFFFF", Stock = 2 },
            new ColorVariant { Id = 8, VehicleId = 3, Name = "Mineral White", Hex = "#F5F5F5", Stock = 2 },
            new ColorVariant { Id = 9, VehicleId = 3, Name = "Black Sapphire", Hex = "#000000", Stock = 2 },
            new ColorVariant { Id = 10, VehicleId = 4, Name = "Glacier White", Hex = "#FFFFFF", Stock = 2 },
            new ColorVariant { Id = 11, VehicleId = 4, Name = "Mythos Black", Hex = "#000000", Stock = 1 },
            new ColorVariant { Id = 12, VehicleId = 4, Name = "Tango Red", Hex = "#C8102E", Stock = 1 },
            new ColorVariant { Id = 13, VehicleId = 5, Name = "Obsidian Black", Hex = "#000000", Stock = 1 },
            new ColorVariant { Id = 14, VehicleId = 5, Name = "Diamond White", Hex = "#FFFFFF", Stock = 1 }
        );
    }
}
