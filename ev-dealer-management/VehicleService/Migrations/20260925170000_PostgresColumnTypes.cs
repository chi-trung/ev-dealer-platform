using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VehicleService.Data;

#nullable disable

namespace VehicleService.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260925170000_PostgresColumnTypes")]
    /// <summary>
    /// Issue #92 (P2): converts this service's SQLite-shaped columns to their
    /// real Postgres types. This file carries the full rationale; the other
    /// five services' PostgresColumnTypes migrations point back here.
    ///
    /// ROOT CAUSE, MEASURED NOT GUESSED
    /// Every migration in this repo was scaffolded while DB_PROVIDER was
    /// unset, and unset means "sqlite" (Common/Data/DbProviderSelector.cs).
    /// EF emits fixed C# at DESIGN time, so 20251101000000_Baseline.cs
    /// declares its date columns as type "TEXT". On a SQLite file that is
    /// honest — SQLite has no date type. On Postgres it is a real `text`
    /// column, while the MODEL at run time renders the same properties as
    /// `timestamp with time zone` (confirmed with `dotnet ef dbcontext
    /// script` under DB_PROVIDER=postgres). Database and running model
    /// disagree about 70 columns across the six DB-backed services.
    ///
    /// MEASURED CONSEQUENCES on a real postgres:16:
    ///
    ///   writes survive — an INSERT carrying a uuid / numeric / timestamptz
    ///   value into a `text` column succeeds, because Postgres casts the
    ///   literal on the way in. This is why the bug is easy to miss.
    ///
    ///   comparisons break — a date filter sends a timestamptz parameter
    ///   against a `text` column, and Postgres has no operator for it:
    ///     SELECT count(*) FROM "Orders" WHERE "CreatedAt" > $1;
    ///     ERROR: operator does not exist: text > timestamp with time zone
    ///   Production code that walks straight into this:
    ///     ReportingService/Endpoints/ReportEndpoints.cs:90,92
    ///     ReportingService/Services/SalesDataService.cs:67,69
    ///     UserService/Services/UserServiceImpl.cs:294,346,398
    ///
    ///   booleans fail immediately — a `bool` model property against an
    ///   `integer` column throws on the first write, so on Postgres
    ///   POST /api/auth/register returned 500 with
    ///     42804: column "IsActive" is of type integer but expression is
    ///            of type boolean
    ///   i.e. a user cannot be registered at all. Fixed in the same batch.
    ///
    /// WHY RAW SQL GATED ON THE PROVIDER
    /// migrationBuilder.AlterColumn() emits a bare
    /// `ALTER COLUMN ... TYPE <t>`, which Postgres rejects in both directions
    /// (measured): text->timestamptz 42804, timestamptz->text 42804,
    /// boolean->integer 42804 — each needs an explicit USING. SQLite cannot
    /// ALTER a column type at all, so the non-Npgsql branch is a no-op: the
    /// SQLite file already has the right shape for SQLite.
    ///
    /// THE EMPTY-STRING GUARD
    /// A text column can hold '' where a date was meant; casting that fails
    /// (invalid input syntax for type timestamp with time zone: ""), and since
    /// EF wraps a migration in one transaction the whole thing rolls back —
    /// no partial damage, but the deploy stops. Nullable columns therefore
    /// cast through NULLIF(TRIM(x), ''), turning '' into NULL. NOT NULL
    /// columns get the plain cast: '' there is already unrepresentable, and
    /// nulling it would violate the constraint anyway, so a blank row is a
    /// loud failure rather than a quiet data edit.
    ///
    /// WHY A NEW MIGRATION ID
    /// The baseline is already in __EFMigrationsHistory on the shared
    /// database, and possibly carrying real rows. Editing an applied
    /// migration does not re-run it — Postgres matches on the id, so a
    /// changed body under the same id is silently skipped. A new id runs.
    /// Down() casts back with the USING clauses Postgres requires, so a
    /// rollback lands on the pre-P2 shape rather than failing halfway.
    ///
    /// This file: Dealers.CreatedAt/UpdatedAt, Vehicles.CreatedAt/UpdatedAt.
    /// All four are NOT NULL, so they take the plain cast.
    /// </summary>
    public partial class PostgresColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "Dealers" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Dealers" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Vehicles" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Vehicles" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "Dealers" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Dealers" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Vehicles" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Vehicles" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");
        }
    }
}
