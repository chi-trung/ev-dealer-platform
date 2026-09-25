using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerService.Migrations
{
    /// <summary>
    /// Issue #92 (P2): this migration is SQLITE-SHAPED and could not ever run
    /// on the production database. render.yaml points this service at
    /// PostgreSQL (evm-postgres / evm_core), and against a real postgres:16
    /// measured in P2 the Up() failed with:
    ///
    ///   42804: column "Amount" cannot be cast automatically to type numeric
    ///   Hint: You might need to specify "USING "Amount"::numeric(18,2)".
    ///
    /// The AlterColumn emits a bare ALTER COLUMN ... TYPE numeric(18,2), and
    /// Postgres refuses an implicit text -> numeric cast. The column arrives
    /// as text because the preceding InitialCustomerServiceMigration declared
    /// it type "TEXT" — legal on SQLite, and on Postgres it becomes a real
    /// `text` column, so the cast has to be spelled out.
    ///
    /// This is why the service was crash-looping (exit 134) on a clean
    /// database: Migrate() runs unguarded here, so a failed migration kills
    /// the process. SalesService has the identical shape and the identical
    /// latent failure — it just has a catch that swallows it, so it reports
    /// healthy while Payments.Amount silently stays text. Both fixed in P2.
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
                    """ALTER TABLE "Purchases" ALTER COLUMN "Amount" TYPE numeric(18,2) USING "Amount"::numeric(18,2);""");
                return;
            }

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "Purchases",
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
                    """ALTER TABLE "Purchases" ALTER COLUMN "Amount" TYPE text USING "Amount"::text;""");
                return;
            }

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "Purchases",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18, 2)");
        }
    }
}
