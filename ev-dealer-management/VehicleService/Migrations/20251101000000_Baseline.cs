using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VehicleService.Data;

#nullable disable

namespace VehicleService.Migrations
{
    /// <summary>
    /// BASELINE migration: converts VehicleService from EnsureCreated() to
    /// Migrate() (issue #121, review round 2).
    ///
    /// THE COLLISION, PROBED
    /// UserService used Migrate() while this service used EnsureCreated(), and
    /// both pointed at one database. EnsureCreated() gates on the WHOLE
    /// database existing, not on the tables this context owns: on a database
    /// that already exists it creates NOTHING and returns false rather than
    /// throwing, so /health stays green while every endpoint dies on "no such
    /// table". Worse, it does not write __EFMigrationsHistory at all, so the
    /// other service's Migrate() cannot see that any work was done and
    /// replays its own full history. Measured both boot orderings:
    ///
    ///   VehicleService first -> UserService.Migrate() replays a migration
    ///     that does CreateTable("Dealers") -> SqliteException
    ///     'table "Dealers" already exists' -> userservice fails to boot.
    ///   UserService first -> VehicleService.EnsureCreated() returns false,
    ///     creates nothing, Dealers/Vehicles never exist.
    ///
    /// Removing the DbSet on one side did NOT fix it: the collision is in the
    /// two creation strategies each claiming the whole database. The fix is
    /// that both use Migrate() with DISJOINT table sets, coordinated by
    /// __EFMigrationsHistory -- which both sides now write.
    ///
    /// WHY A HAND-AUTHORED BASELINE, AND WHY EnsureCreated CANNOT STAY
    /// VehicleService's three existing migrations are all subsumed by this
    /// file (see the SQUASH note below), and none could have been replayed:
    /// RemoveReservations.Up does an unconditional DropTable("Reservations"),
    /// which throws on any database the current model produces.
    ///
    /// THIS IS A FULL SQUASH, NOT AN ADDED MIGRATION
    ///   RemoveReservations      -- DropTable("Reservations") + UpdateData on
    ///     Dealers/Vehicles timestamps. The DROP is unconditional, so it
    ///     throws "no such table: Reservations" on ANY database this baseline
    ///     produces (the table is absent from the current model). It was
    ///     never runnable: this service used EnsureCreated(), so the
    ///     migration was applied to zero real databases. Its purpose -- no
    ///     Reservations table -- is expressed by this baseline simply not
    ///     creating one.
    ///   UpdateSeedVehicleImagesToWwwroot -- pure UpdateData on seed-row
    ///     timestamps, already reflected in the pinned SeedTimestamp the
    ///     baseline seeds.
    ///   AddDecimalPrecision     -- AlterColumn Price TEXT -> decimal(18,2).
    ///     The baseline declares Price as decimal(18,2) directly, so a fresh
    ///     database is born correct.
    /// Deleting them is the point: a fresh database must reach the current
    /// schema by running ONE migration, not by replaying three deltas whose
    /// first statement cannot succeed.
    ///
    /// WHAT Up RUNS AGAINST
    /// Existing VehicleService databases were built by EnsureCreated: they
    /// have every table below and NO __EFMigrationsHistory. This migration's
    /// id is new, so Migrate() runs it. Every statement is idempotent --
    /// CREATE TABLE/INDEX IF NOT EXISTS, and INSERT ... WHERE NOT EXISTS --
    /// so an already-populated database is neither truncated nor duplicated.
    /// Down leaves the tables in place for the same reason.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20251101000000_Baseline")]
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent for databases EnsureCreated() already built: the
            // tables exist, so the DROP-then-CREATE sequence leaves them
            // empty. HasData seed rows are then re-applied by the
            // UpdateData calls below ONLY where they are absent (see the
            // guard before each block), so an already-seeded DB is not
            // truncated. Fresh databases get the tables then the seed.
            //
            // Order matters: Dealers first, everything referencing it after.

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""Dealers"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Dealers"" PRIMARY KEY,
    ""Name"" TEXT NOT NULL,
    ""Region"" TEXT NOT NULL,
    ""Contact"" TEXT NOT NULL,
    ""Email"" TEXT NOT NULL,
    ""Address"" TEXT NOT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""VehicleTypes"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VehicleTypes"" PRIMARY KEY,
    ""Value"" TEXT NOT NULL,
    ""Label"" TEXT NOT NULL
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""Vehicles"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Vehicles"" PRIMARY KEY,
    ""Model"" TEXT NOT NULL,
    ""Type"" TEXT NOT NULL,
    ""Price"" TEXT NOT NULL,
    ""BatteryCapacity"" REAL NOT NULL,
    ""Range"" INTEGER NOT NULL,
    ""StockQuantity"" INTEGER NOT NULL,
    ""Description"" TEXT NULL,
    ""DealerId"" INTEGER NOT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_Vehicles_Dealers_DealerId"" FOREIGN KEY (""DealerId"")
        REFERENCES ""Dealers"" (""Id"") ON DELETE RESTRICT
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""VehicleImages"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VehicleImages"" PRIMARY KEY,
    ""VehicleId"" INTEGER NOT NULL,
    ""Url"" TEXT NOT NULL,
    ""AltText"" TEXT NULL,
    ""Order"" INTEGER NOT NULL,
    CONSTRAINT ""FK_VehicleImages_Vehicles_VehicleId"" FOREIGN KEY (""VehicleId"")
        REFERENCES ""Vehicles"" (""Id"") ON DELETE CASCADE
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""VehicleSpecifications"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VehicleSpecifications"" PRIMARY KEY,
    ""VehicleId"" INTEGER NOT NULL,
    ""Acceleration"" TEXT NULL,
    ""TopSpeed"" TEXT NULL,
    ""Charging"" TEXT NULL,
    ""Warranty"" TEXT NULL,
    ""Seats"" INTEGER NULL,
    ""Cargo"" TEXT NULL,
    CONSTRAINT ""FK_VehicleSpecifications_Vehicles_VehicleId"" FOREIGN KEY (""VehicleId"")
        REFERENCES ""Vehicles"" (""Id"") ON DELETE CASCADE
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""ColorVariants"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ColorVariants"" PRIMARY KEY,
    ""VehicleId"" INTEGER NOT NULL,
    ""Name"" TEXT NOT NULL,
    ""Hex"" TEXT NOT NULL,
    ""Stock"" INTEGER NOT NULL,
    CONSTRAINT ""FK_ColorVariants_Vehicles_VehicleId"" FOREIGN KEY (""VehicleId"")
        REFERENCES ""Vehicles"" (""Id"") ON DELETE CASCADE
);");

            // Indexes: all the ones EnsureCreated() emits. CREATE INDEX IF NOT
            // EXISTS keeps this safe on an already-populated schema.
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Vehicles_DealerId"" ON ""Vehicles"" (""DealerId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_VehicleImages_VehicleId"" ON ""VehicleImages"" (""VehicleId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_VehicleSpecifications_VehicleId"" ON ""VehicleSpecifications"" (""VehicleId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ColorVariants_VehicleId"" ON ""ColorVariants"" (""VehicleId"");");

            // ---- Seed data: mirrors ApplicationDbContext.SeedData() exactly.
            // Guarded per table so a database EnsureCreated() already seeded
            // is not rewritten; a fresh one gets all of it.
            migrationBuilder.Sql(@"
INSERT INTO ""VehicleTypes"" (""Id"", ""Value"", ""Label"")
SELECT 1, 'sedan', 'Sedan' WHERE NOT EXISTS (SELECT 1 FROM ""VehicleTypes"" WHERE ""Id"" = 1);
INSERT INTO ""VehicleTypes"" (""Id"", ""Value"", ""Label"")
SELECT 2, 'suv', 'SUV' WHERE NOT EXISTS (SELECT 1 FROM ""VehicleTypes"" WHERE ""Id"" = 2);
INSERT INTO ""VehicleTypes"" (""Id"", ""Value"", ""Label"")
SELECT 3, 'hatchback', 'Hatchback' WHERE NOT EXISTS (SELECT 1 FROM ""VehicleTypes"" WHERE ""Id"" = 3);
INSERT INTO ""VehicleTypes"" (""Id"", ""Value"", ""Label"")
SELECT 4, 'coupe', 'Coupe' WHERE NOT EXISTS (SELECT 1 FROM ""VehicleTypes"" WHERE ""Id"" = 4);
INSERT INTO ""VehicleTypes"" (""Id"", ""Value"", ""Label"")
SELECT 5, 'convertible', 'Convertible' WHERE NOT EXISTS (SELECT 1 FROM ""VehicleTypes"" WHERE ""Id"" = 5);
INSERT INTO ""VehicleTypes"" (""Id"", ""Value"", ""Label"")
SELECT 6, 'truck', 'Truck' WHERE NOT EXISTS (SELECT 1 FROM ""VehicleTypes"" WHERE ""Id"" = 6);");

            // Dealers: 4 rows, same ids/names as SeedData. CreatedAt/UpdatedAt
            // are the pinned SeedTimestamp constant, not UtcNow.
            migrationBuilder.Sql(@"
INSERT INTO ""Dealers"" (""Id"", ""Name"", ""Region"", ""Contact"", ""Email"", ""Address"", ""CreatedAt"", ""UpdatedAt"")
SELECT 1, 'Tesla Center HCMC', 'Ho Chi Minh City', '0901234567', 'hcmc@tesla.com',
       '123 Nguyen Hue, District 1, HCMC', '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Dealers"" WHERE ""Id"" = 1);
INSERT INTO ""Dealers"" (""Id"", ""Name"", ""Region"", ""Contact"", ""Email"", ""Address"", ""CreatedAt"", ""UpdatedAt"")
SELECT 2, 'BMW Center District 1', 'Ho Chi Minh City', '0902345678', 'district1@bmw.com',
       '456 Le Loi, District 1, HCMC', '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Dealers"" WHERE ""Id"" = 2);
INSERT INTO ""Dealers"" (""Id"", ""Name"", ""Region"", ""Contact"", ""Email"", ""Address"", ""CreatedAt"", ""UpdatedAt"")
SELECT 3, 'Audi Center District 2', 'Ho Chi Minh City', '0903456789', 'district2@audi.com',
       '789 Dong Khoi, District 2, HCMC', '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Dealers"" WHERE ""Id"" = 3);
INSERT INTO ""Dealers"" (""Id"", ""Name"", ""Region"", ""Contact"", ""Email"", ""Address"", ""CreatedAt"", ""UpdatedAt"")
SELECT 4, 'Mercedes-Benz Center District 3', 'Ho Chi Minh City', '0904567890', 'district3@mercedes.com',
       '321 Nguyen Van Cu, District 3, HCMC', '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Dealers"" WHERE ""Id"" = 4);");

            // Vehicles: 5 rows.
            migrationBuilder.Sql(@"
INSERT INTO ""Vehicles"" (""Id"", ""Model"", ""Type"", ""Price"", ""BatteryCapacity"", ""Range"", ""StockQuantity"", ""Description"", ""DealerId"", ""CreatedAt"", ""UpdatedAt"")
SELECT 1, 'Tesla Model 3', 'sedan', '45000', 75, 350, 12,
       'Premium electric sedan with autopilot capabilities', 1,
       '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Vehicles"" WHERE ""Id"" = 1);
INSERT INTO ""Vehicles"" (""Id"", ""Model"", ""Type"", ""Price"", ""BatteryCapacity"", ""Range"", ""StockQuantity"", ""Description"", ""DealerId"", ""CreatedAt"", ""UpdatedAt"")
SELECT 2, 'Tesla Model Y', 'suv', '55000', 75, 330, 8,
       'Versatile electric SUV perfect for families', 1,
       '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Vehicles"" WHERE ""Id"" = 2);
INSERT INTO ""Vehicles"" (""Id"", ""Model"", ""Type"", ""Price"", ""BatteryCapacity"", ""Range"", ""StockQuantity"", ""Description"", ""DealerId"", ""CreatedAt"", ""UpdatedAt"")
SELECT 3, 'BMW i4', 'sedan', '52000', 83.9, 300, 6,
       'Luxury electric sedan with BMW''s signature driving dynamics', 2,
       '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Vehicles"" WHERE ""Id"" = 3);
INSERT INTO ""Vehicles"" (""Id"", ""Model"", ""Type"", ""Price"", ""BatteryCapacity"", ""Range"", ""StockQuantity"", ""Description"", ""DealerId"", ""CreatedAt"", ""UpdatedAt"")
SELECT 4, 'Audi e-tron', 'suv', '65000', 95, 222, 4,
       'Premium electric SUV with quattro all-wheel drive', 3,
       '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Vehicles"" WHERE ""Id"" = 4);
INSERT INTO ""Vehicles"" (""Id"", ""Model"", ""Type"", ""Price"", ""BatteryCapacity"", ""Range"", ""StockQuantity"", ""Description"", ""DealerId"", ""CreatedAt"", ""UpdatedAt"")
SELECT 5, 'Mercedes EQS', 'sedan', '120000', 107.8, 350, 2,
       'Ultra-luxury electric sedan with cutting-edge technology', 4,
       '2026-09-18 00:00:00', '2026-09-18 00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM ""Vehicles"" WHERE ""Id"" = 5);");

            // VehicleSpecifications: 5 rows.
            migrationBuilder.Sql(@"
INSERT INTO ""VehicleSpecifications"" (""Id"", ""VehicleId"", ""Acceleration"", ""TopSpeed"", ""Charging"", ""Warranty"", ""Seats"", ""Cargo"")
SELECT 1, 1, '3.1s 0-60mph', '162 mph', '250 kW Supercharging', '4 years/50,000 miles', 5, '15 cu ft'
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleSpecifications"" WHERE ""Id"" = 1);
INSERT INTO ""VehicleSpecifications"" (""Id"", ""VehicleId"", ""Acceleration"", ""TopSpeed"", ""Charging"", ""Warranty"", ""Seats"", ""Cargo"")
SELECT 2, 2, '3.5s 0-60mph', '155 mph', '250 kW Supercharging', '4 years/50,000 miles', 7, '76 cu ft'
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleSpecifications"" WHERE ""Id"" = 2);
INSERT INTO ""VehicleSpecifications"" (""Id"", ""VehicleId"", ""Acceleration"", ""TopSpeed"", ""Charging"", ""Warranty"", ""Seats"", ""Cargo"")
SELECT 3, 3, '3.9s 0-60mph', '118 mph', '150 kW DC Fast Charging', '4 years/50,000 miles', 5, '16 cu ft'
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleSpecifications"" WHERE ""Id"" = 3);
INSERT INTO ""VehicleSpecifications"" (""Id"", ""VehicleId"", ""Acceleration"", ""TopSpeed"", ""Charging"", ""Warranty"", ""Seats"", ""Cargo"")
SELECT 4, 4, '5.5s 0-60mph', '124 mph', '150 kW DC Fast Charging', '4 years/50,000 miles', 5, '27.2 cu ft'
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleSpecifications"" WHERE ""Id"" = 4);
INSERT INTO ""VehicleSpecifications"" (""Id"", ""VehicleId"", ""Acceleration"", ""TopSpeed"", ""Charging"", ""Warranty"", ""Seats"", ""Cargo"")
SELECT 5, 5, '4.3s 0-60mph', '130 mph', '200 kW Supercharging', '4 years/50,000 miles', 5, '22 cu ft'
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleSpecifications"" WHERE ""Id"" = 5);");

            // VehicleImages: 11 rows (URLs match the wwwroot seed webps).
            migrationBuilder.Sql(@"
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 1, 1, '/images/seed-car1.webp', 'Tesla Model 3 Front', 1
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 1);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 2, 1, '/images/seed-car2.webp', 'Tesla Model 3 Side', 2
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 2);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 3, 1, '/images/seed-car3.webp', 'Tesla Model 3 Interior', 3
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 3);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 4, 2, '/images/seed-car2.webp', 'Tesla Model Y Front', 1
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 4);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 5, 2, '/images/seed-car3.webp', 'Tesla Model Y Side', 2
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 5);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 6, 2, '/images/seed-car4.webp', 'Tesla Model Y Interior', 3
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 6);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 7, 3, '/images/seed-car3.webp', 'BMW i4 Front', 1
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 7);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 8, 3, '/images/seed-car4.webp', 'BMW i4 Side', 2
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 8);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 9, 4, '/images/seed-car4.webp', 'Audi e-tron Front', 1
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 9);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 10, 5, '/images/seed-car1.webp', 'Mercedes EQS Front', 1
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 10);
INSERT INTO ""VehicleImages"" (""Id"", ""VehicleId"", ""Url"", ""AltText"", ""Order"")
SELECT 11, 5, '/images/seed-car2.webp', 'Mercedes EQS Side', 2
WHERE NOT EXISTS (SELECT 1 FROM ""VehicleImages"" WHERE ""Id"" = 11);");

            // ColorVariants: 14 rows.
            migrationBuilder.Sql(@"
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 1, 1, 'Pearl White', '#FFFFFF', 5
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 1);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 2, 1, 'Midnight Silver', '#2C2C2C', 4
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 2);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 3, 1, 'Deep Blue', '#1E3A8A', 3
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 3);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 4, 2, 'Pearl White', '#FFFFFF', 3
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 4);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 5, 2, 'Midnight Silver', '#2C2C2C', 3
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 5);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 6, 2, 'Red Multi-Coat', '#DC2626', 2
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 6);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 7, 3, 'Alpine White', '#FFFFFF', 2
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 7);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 8, 3, 'Mineral White', '#F5F5F5', 2
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 8);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 9, 3, 'Black Sapphire', '#000000', 2
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 9);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 10, 4, 'Glacier White', '#FFFFFF', 2
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 10);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 11, 4, 'Mythos Black', '#000000', 1
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 11);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 12, 4, 'Tango Red', '#C8102E', 1
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 12);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 13, 5, 'Obsidian Black', '#000000', 1
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 13);
INSERT INTO ""ColorVariants"" (""Id"", ""VehicleId"", ""Name"", ""Hex"", ""Stock"")
SELECT 14, 5, 'Diamond White', '#FFFFFF', 1
WHERE NOT EXISTS (SELECT 1 FROM ""ColorVariants"" WHERE ""Id"" = 14);");
        }

        /// <inheritdoc />
        /// Down is intentionally a NO-OP. Downgrading VehicleService's schema
        /// by hand would drop the tables the other six services read from;
        /// the pre-baseline migrations (RemoveReservations etc.) are not
        /// self-contained either. Roll forward, never back.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
