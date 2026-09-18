using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehicleService.Migrations
{
    /// <inheritdoc />
    public partial class AddDecimalPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only the column-type change. The scaffolder also emitted UpdateData
            // rows rewriting Dealers/Vehicles CreatedAt/UpdatedAt; those came from
            // the seed timestamps moving from DateTime.UtcNow to the pinned
            // SeedTimestamp constant (see ApplicationDbContext). They are dropped
            // here because rewriting seeded-row timestamps is a data change, not a
            // schema one, and the rows in Down could only ever restore
            // tick-precision values that no real database actually holds.
            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "Vehicles",
                type: "decimal(18, 2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "Vehicles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18, 2)");
        }
    }
}
