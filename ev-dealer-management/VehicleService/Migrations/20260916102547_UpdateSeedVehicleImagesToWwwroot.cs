using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehicleService.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSeedVehicleImagesToWwwroot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5177), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5177) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5179), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5180) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5182), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5182) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5184), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5184) });

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 1,
                column: "Url",
                value: "/images/seed-car1.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 2,
                column: "Url",
                value: "/images/seed-car2.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 3,
                column: "Url",
                value: "/images/seed-car3.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 4,
                column: "Url",
                value: "/images/seed-car2.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 5,
                column: "Url",
                value: "/images/seed-car3.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 6,
                column: "Url",
                value: "/images/seed-car4.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 7,
                column: "Url",
                value: "/images/seed-car3.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 8,
                column: "Url",
                value: "/images/seed-car4.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 9,
                column: "Url",
                value: "/images/seed-car4.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 10,
                column: "Url",
                value: "/images/seed-car1.webp");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 11,
                column: "Url",
                value: "/images/seed-car2.webp");

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5223), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5223) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5226), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5226) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5229), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5229) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5232), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5232) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5234), new DateTime(2026, 9, 16, 10, 25, 47, 563, DateTimeKind.Utc).AddTicks(5235) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6483), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6484) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6487), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6489) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6491), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6492) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6494), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6495) });

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 1,
                column: "Url",
                value: "/src/assets/img/car1.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 2,
                column: "Url",
                value: "/src/assets/img/car2.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 3,
                column: "Url",
                value: "/src/assets/img/car3.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 4,
                column: "Url",
                value: "/src/assets/img/car2.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 5,
                column: "Url",
                value: "/src/assets/img/car3.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 6,
                column: "Url",
                value: "/src/assets/img/car4.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 7,
                column: "Url",
                value: "/src/assets/img/car3.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 8,
                column: "Url",
                value: "/src/assets/img/car4.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 9,
                column: "Url",
                value: "/src/assets/img/car4.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 10,
                column: "Url",
                value: "/src/assets/img/car1.png");

            migrationBuilder.UpdateData(
                table: "VehicleImages",
                keyColumn: "Id",
                keyValue: 11,
                column: "Url",
                value: "/src/assets/img/car2.png");

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6534), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6534) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6539), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6540) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6543), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6544) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6601), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6602) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6605), new DateTime(2025, 11, 24, 9, 47, 46, 921, DateTimeKind.Utc).AddTicks(6605) });
        }
    }
}
