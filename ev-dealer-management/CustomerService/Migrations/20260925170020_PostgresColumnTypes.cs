using CustomerService.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerService.Migrations
{
    [DbContext(typeof(CustomerDbContext))]
    [Migration("20260925170020_PostgresColumnTypes")]
    /// <summary>
    /// Issue #92 (P2): SQLite-shaped columns -> real Postgres types.
    /// Full rationale in VehicleService/Migrations/20260925170000_PostgresColumnTypes.cs.
    ///
    /// All ten columns here are timestamps, so the damage is confined to date
    /// comparison rather than to writes. Two are nullable and carry the
    /// NULLIF(TRIM(x),'') guard: Complaints.ResolvedAt and
    /// Customers.UpdatedAt.
    ///
    /// Purchases.Amount is deliberately NOT here — the sibling migration
    /// 20260918020145_AddDecimalPrecision converts it, with its own USING
    /// clause for the same 42804 reason. Two migrations touching one column
    /// would mean one of them is a no-op, and the history would be lying
    /// about the order things happened.
    /// </summary>
    public partial class PostgresColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "ComplaintAttachments" ALTER COLUMN "UploadedAt" TYPE timestamptz USING "UploadedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "ComplaintHistoryEntries" ALTER COLUMN "ActionDate" TYPE timestamptz USING "ActionDate"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "Complaints" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Complaints" ALTER COLUMN "ResolvedAt" TYPE timestamptz USING NULLIF(TRIM("ResolvedAt"), '')::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Complaints" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "Customers" ALTER COLUMN "JoinDate" TYPE timestamptz USING "JoinDate"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Customers" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING NULLIF(TRIM("UpdatedAt"), '')::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "Purchases" ALTER COLUMN "PurchaseDate" TYPE timestamptz USING "PurchaseDate"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "TestDrives" ALTER COLUMN "AppointmentDate" TYPE timestamptz USING "AppointmentDate"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "TestDrives" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "ComplaintAttachments" ALTER COLUMN "UploadedAt" TYPE text USING "UploadedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "ComplaintHistoryEntries" ALTER COLUMN "ActionDate" TYPE text USING "ActionDate"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "Complaints" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Complaints" ALTER COLUMN "ResolvedAt" TYPE text USING "ResolvedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Complaints" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "Customers" ALTER COLUMN "JoinDate" TYPE text USING "JoinDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Customers" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "Purchases" ALTER COLUMN "PurchaseDate" TYPE text USING "PurchaseDate"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "TestDrives" ALTER COLUMN "AppointmentDate" TYPE text USING "AppointmentDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "TestDrives" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
        }
    }
}
