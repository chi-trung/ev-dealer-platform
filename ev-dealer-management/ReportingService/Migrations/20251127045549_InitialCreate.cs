using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportingService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DebtSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DealerId = table.Column<int>(type: "INTEGER", nullable: true),
                    DealerName = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", nullable: true),
                    DebtType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReferenceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReferenceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18, 2)", nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "decimal(18, 2)", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventorySummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehicleId = table.Column<int>(type: "INTEGER", nullable: false),
                    VehicleName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DealerId = table.Column<int>(type: "INTEGER", nullable: false),
                    DealerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StockCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                       .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    From = table.Column<DateTime>(type: "TEXT", nullable: true),
                    To = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RequestedBy = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DealerId = table.Column<int>(type: "INTEGER", nullable: false),
                    DealerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SalespersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    SalespersonName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    TotalOrders = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalRevenue = table.Column<decimal>(type: "decimal(18, 2)", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportExports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                       .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportRequestId = table.Column<int>(type: "INTEGER", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportExports_ReportRequests_ReportRequestId",
                        column: x => x.ReportRequestId,
                        principalTable: "ReportRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DebtSummaries_CustomerId",
                table: "DebtSummaries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtSummaries_DealerId",
                table: "DebtSummaries",
                column: "DealerId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtSummaries_DebtType_ReferenceType_ReferenceId",
                table: "DebtSummaries",
                columns: new[] { "DebtType", "ReferenceType", "ReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventorySummaries_DealerId_VehicleId",
                table: "InventorySummaries",
                columns: new[] { "DealerId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportExports_ReportRequestId",
                table: "ReportExports",
                column: "ReportRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesSummaries_Date_DealerId_SalespersonId",
                table: "SalesSummaries",
                columns: new[] { "Date", "DealerId", "SalespersonId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DebtSummaries");

            migrationBuilder.DropTable(
                name: "InventorySummaries");

            migrationBuilder.DropTable(
                name: "ReportExports");

            migrationBuilder.DropTable(
                name: "SalesSummaries");

            migrationBuilder.DropTable(
                name: "ReportRequests");
        }
    }
}
