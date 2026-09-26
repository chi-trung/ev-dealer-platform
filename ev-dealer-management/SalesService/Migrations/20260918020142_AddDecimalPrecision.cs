using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesService.Migrations
{
    /// <summary>
    /// Issue #92 (P2): this migration is SQLITE-SHAPED and could not ever run
    /// on the production database. render.yaml points this service at
    /// PostgreSQL (evm-postgres / evm_core), and against a real postgres:16
    /// measured in P2 the Up() failed with:
    ///
    ///   42804: column "Value" cannot be cast automatically to type numeric
    ///   Hint: You might need to specify "USING "Value"::numeric(18,2)".
    ///
    /// The AlterColumn emits a bare ALTER COLUMN ... TYPE numeric(18,2), and
    /// Postgres refuses an implicit text -> numeric cast. Both columns arrive
    /// as text because the preceding UpdateValueToDecimal migration declared
    /// them type "TEXT" — legal on SQLite, and on Postgres they become real
    /// `text` columns, so the cast has to be spelled out.
    ///
    /// This one hid better than CustomerService's identical bug. Program.cs
    /// wraps Migrate() in a try/catch that logs a warning and continues, so
    /// the service reported HEALTHY while Payments.Amount and Promotions.Value
    /// silently stayed `text` on the deploy — measured in P2 via
    /// information_schema after a clean boot. CustomerService has the same
    /// migration shape without the catch, so it crash-looped (exit 134) and
    /// the failure was at least visible. Both fixed here.
    ///
    /// The USING clause is safe on SQLite too: SQLite accepts
    /// ALTER TABLE ... RENAME/ADD COLUMN but NOT ALTER COLUMN, so Npgsql-only
    /// SQL is gated on the provider rather than being emitted unconditionally.
    /// The default (no USING) branch keeps SQLite's own statement unchanged.
    /// </summary>
    public partial class AddDecimalPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """ALTER TABLE "Promotions" ALTER COLUMN "Value" TYPE numeric(18,2) USING "Value"::numeric(18,2);""");
                migrationBuilder.Sql(
                    """ALTER TABLE "Payments" ALTER COLUMN "Amount" TYPE numeric(18,2) USING "Amount"::numeric(18,2);""");
                return;
            }

            migrationBuilder.AlterColumn<decimal>(
                name: "Value",
                table: "Promotions",
                type: "decimal(18, 2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "Payments",
                type: "decimal(18, 2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """ALTER TABLE "Promotions" ALTER COLUMN "Value" TYPE text USING "Value"::text;""");
                migrationBuilder.Sql(
                    """ALTER TABLE "Payments" ALTER COLUMN "Amount" TYPE text USING "Amount"::text;""");
                return;
            }

            migrationBuilder.AlterColumn<decimal>(
                name: "Value",
                table: "Promotions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18, 2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "Payments",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18, 2)");
        }
    }
}
