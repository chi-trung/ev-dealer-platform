using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UserService.Data;

#nullable disable

namespace UserService.Migrations
{
    [DbContext(typeof(UserDbContext))]
    [Migration("20260925170010_PostgresColumnTypes")]
    /// <summary>
    /// Issue #92 (P2): SQLite-shaped columns -> real Postgres types.
    /// Full rationale in VehicleService/Migrations/20260925170000_PostgresColumnTypes.cs.
    ///
    /// This service carries the sharpest symptom in the whole P2 audit:
    /// Users.IsActive is `integer` in the database but `bool` in the model, so
    /// the first INSERT into Users fails with
    ///   42804: column "IsActive" is of type integer but expression is of type boolean
    /// and POST /api/auth/register returns 500. You cannot register a user on
    /// Postgres at all until this migration runs — login is downstream of it.
    ///
    /// Columns: Users.IsActive (bool), Users.CreatedAt/UpdatedAt (timestamptz),
    /// PasswordResetTokens.IsUsed (bool), .ExpiresAt/.CreatedAt (timestamptz),
    /// .UsedAt (timestamptz, NULLABLE -> NULLIF guard).
    /// </summary>
    public partial class PostgresColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "Users" ALTER COLUMN "IsActive" TYPE boolean USING ("IsActive" <> 0);""");
            migrationBuilder.Sql("""ALTER TABLE "Users" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Users" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "PasswordResetTokens" ALTER COLUMN "IsUsed" TYPE boolean USING ("IsUsed" <> 0);""");
            migrationBuilder.Sql("""ALTER TABLE "PasswordResetTokens" ALTER COLUMN "ExpiresAt" TYPE timestamptz USING "ExpiresAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "PasswordResetTokens" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "PasswordResetTokens" ALTER COLUMN "UsedAt" TYPE timestamptz USING NULLIF(TRIM("UsedAt"), '')::timestamptz;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "Users" ALTER COLUMN "IsActive" TYPE integer USING CASE WHEN "IsActive" THEN 1 ELSE 0 END;""");
            migrationBuilder.Sql("""ALTER TABLE "Users" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Users" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "PasswordResetTokens" ALTER COLUMN "IsUsed" TYPE integer USING CASE WHEN "IsUsed" THEN 1 ELSE 0 END;""");
            migrationBuilder.Sql("""ALTER TABLE "PasswordResetTokens" ALTER COLUMN "ExpiresAt" TYPE text USING "ExpiresAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "PasswordResetTokens" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "PasswordResetTokens" ALTER COLUMN "UsedAt" TYPE text USING "UsedAt"::text;""");
        }
    }
}
