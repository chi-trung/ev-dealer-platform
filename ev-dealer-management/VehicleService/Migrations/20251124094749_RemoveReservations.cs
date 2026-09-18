using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehicleService.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reservations");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                       .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColorVariantId = table.Column<int>(type: "INTEGER", nullable: true),
                    VehicleId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CustomerEmail = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CustomerPhone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TotalPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reservations_ColorVariants_ColorVariantId",
                        column: x => x.ColorVariantId,
                        principalTable: "ColorVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Reservations_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2392), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2395) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2406), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2407) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2411), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2412) });

            migrationBuilder.UpdateData(
                table: "Dealers",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2417), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2418) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2513), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2515) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2521), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2522) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2527), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2528) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2537), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2537) });

            migrationBuilder.UpdateData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2542), new DateTime(2025, 11, 21, 7, 42, 40, 297, DateTimeKind.Utc).AddTicks(2542) });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ColorVariantId",
                table: "Reservations",
                column: "ColorVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_VehicleId",
                table: "Reservations",
                column: "VehicleId");
        }
    }
}
