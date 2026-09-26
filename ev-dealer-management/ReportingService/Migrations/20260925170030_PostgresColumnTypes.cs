using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ev_dealer_reporting.Data;

#nullable disable

namespace ReportingService.Migrations
{
    [DbContext(typeof(ReportingDbContext))]
    [Migration("20260925170030_PostgresColumnTypes")]
    /// <summary>
    /// Issue #92 (P2): SQLite-shaped columns -> real Postgres types.
    /// Full rationale in VehicleService/Migrations/20260925170000_PostgresColumnTypes.cs.
    ///
    /// This service is where the date-filter breakage is most expensive.
    /// DebtSummaries.CreatedAt and SalesSummaries.Date are the columns behind
    /// every date-ranged report, and both are `text` today, so
    /// GET /api/reports/debt-summary?from=...&to=... filters with
    ///   query.Where(d => d.CreatedAt >= fromDate.Value)
    /// which Npgsql sends as a timestamptz parameter against a `text` column —
    /// "operator does not exist: text > timestamp with time zone", caught by
    /// the endpoint's catch-all and returned as a 500. The reporting surface
    /// is non-functional with a date range until this runs.
    ///
    /// Also here: three Guid primary keys (DebtSummaries.Id,
    /// InventorySummaries.Id, SalesSummaries.Id) and ReportExports.SizeBytes,
    /// an int in the database against a long in the model. The uuid casts are
    /// NOT NULL columns, so a blank or malformed Guid row is a loud failure
    /// rather than a quiet data edit.
    ///
    /// Four nullable timestamps carry the NULLIF(TRIM(x),'') guard:
    /// DebtSummaries.DueDate, ReportRequests.From/To/CompletedAt.
    /// </summary>
    public partial class PostgresColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "DebtSummaries" ALTER COLUMN "Id" TYPE uuid USING "Id"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "DebtSummaries" ALTER COLUMN "DueDate" TYPE timestamptz USING NULLIF(TRIM("DueDate"), '')::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "DebtSummaries" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "DebtSummaries" ALTER COLUMN "LastUpdatedAt" TYPE timestamptz USING "LastUpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "InventorySummaries" ALTER COLUMN "Id" TYPE uuid USING "Id"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "InventorySummaries" ALTER COLUMN "LastUpdatedAt" TYPE timestamptz USING "LastUpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "ReportExports" ALTER COLUMN "SizeBytes" TYPE bigint USING "SizeBytes"::bigint;""");
            migrationBuilder.Sql("""ALTER TABLE "ReportExports" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "ReportRequests" ALTER COLUMN "From" TYPE timestamptz USING NULLIF(TRIM("From"), '')::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "ReportRequests" ALTER COLUMN "To" TYPE timestamptz USING NULLIF(TRIM("To"), '')::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "ReportRequests" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "ReportRequests" ALTER COLUMN "CompletedAt" TYPE timestamptz USING NULLIF(TRIM("CompletedAt"), '')::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "SalesSummaries" ALTER COLUMN "Id" TYPE uuid USING "Id"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "SalesSummaries" ALTER COLUMN "Date" TYPE timestamptz USING "Date"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "SalesSummaries" ALTER COLUMN "LastUpdatedAt" TYPE timestamptz USING "LastUpdatedAt"::timestamptz;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "DebtSummaries" ALTER COLUMN "Id" TYPE text USING "Id"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "DebtSummaries" ALTER COLUMN "DueDate" TYPE text USING "DueDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "DebtSummaries" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "DebtSummaries" ALTER COLUMN "LastUpdatedAt" TYPE text USING "LastUpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "InventorySummaries" ALTER COLUMN "Id" TYPE text USING "Id"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "InventorySummaries" ALTER COLUMN "LastUpdatedAt" TYPE text USING "LastUpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "ReportExports" ALTER COLUMN "SizeBytes" TYPE integer USING "SizeBytes"::integer;""");
            migrationBuilder.Sql("""ALTER TABLE "ReportExports" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "ReportRequests" ALTER COLUMN "From" TYPE text USING "From"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "ReportRequests" ALTER COLUMN "To" TYPE text USING "To"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "ReportRequests" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "ReportRequests" ALTER COLUMN "CompletedAt" TYPE text USING "CompletedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "SalesSummaries" ALTER COLUMN "Id" TYPE text USING "Id"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "SalesSummaries" ALTER COLUMN "Date" TYPE text USING "Date"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "SalesSummaries" ALTER COLUMN "LastUpdatedAt" TYPE text USING "LastUpdatedAt"::text;""");
        }
    }
}
