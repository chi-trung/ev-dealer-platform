using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NotificationService.Migrations
{
    /// <summary>
    /// BASELINE migration: converts NotificationService from EnsureCreated()
    /// to Migrate() (Issue #92, P2).
    ///
    /// THE COLLISION, AND WHY IT IS A PRODUCTION BUG TODAY
    /// render.yaml wires ALL SIX database-backed services to ONE Postgres
    /// database (evm-postgres / databaseName: evm_core, referenced at lines
    /// 188, 265, 303, 345, 387 and 427). The services hold DISJOINT table
    /// sets — VehicleService owns Dealers, UserService deliberately does not
    /// map it (Issue #121) — so sharing one database is the design.
    ///
    /// EnsureCreated() gates on whether the DATABASE has any tables, not on
    /// whether this context's own tables exist. On Render the database is
    /// never empty when NotificationService boots: whichever of the other
    /// five services started first has already created its own tables. So
    /// EnsureCreated() returns false having created NOTHING.
    ///
    /// The explicit CREATE TABLE IF NOT EXISTS that followed it in Program.cs
    /// covered NotificationPreferences ONLY, so DeviceTokens — the table all
    /// 14 queue consumers read on every push lookup — was the one that went
    /// missing, and every event silently degraded to log-only, which is
    /// exactly the pre-Issue-#33 behaviour #33 was opened to fix. Measured on
    /// a shared database by EnsureCreatedSharedDatabaseTests.
    ///
    /// On a FRESH database (local dev, CI, a brand-new Render instance where
    /// NotificationService happened to win the race) EnsureCreated did create
    /// both tables and everything worked — which is why this survived review.
    /// The failure needs a NON-EMPTY shared database, i.e. exactly production.
    ///
    /// WHY A HAND-AUTHORED BASELINE
    /// This service has never had a migration, so nothing could be replayed
    /// or squashed: the schema has only ever been produced by EnsureCreated(),
    /// which writes no __EFMigrationsHistory at all. This file is the
    /// authoritative first migration, generated from the real model by
    /// `dotnet ef migrations add` — not transcribed by hand, so it cannot
    /// drift from NotificationDbContext.
    ///
    /// CreateTable OPERATIONS, not raw DDL: each provider's own type mapper
    /// renders the columns correctly (TEXT/INTEGER on SQLite,
    /// text/integer/boolean/timestamp with time zone on Postgres). The raw
    /// SQLite-shaped DDL that used to live in Program.cs would have created
    /// the preferences booleans as INTEGER on the Postgres deploy.
    ///
    /// Int columns carry BOTH annotations, and the Npgsql one is
    /// LOAD-BEARING — verified in this PR against a real postgres:16, not
    /// assumed. Applying this migration to an empty database and inspecting
    /// information_schema gave:
    ///
    ///   column_name | data_type | column_default | is_identity
    ///   Id          | integer   |                | NO
    ///
    /// and the first INSERT that omitted Id failed with
    /// "null value in column \"Id\" of relation \"DeviceTokens\" violates
    /// not-null constraint". Without Npgsql:ValueGenerationStrategy the
    /// Postgres deploy has no sequence and every insert fails.
    ///
    /// This is worth recording because Common/Data/DbProviderSelector.cs
    /// (lines 19-26) asserts the OPPOSITE — that Npgsql's convention layer
    /// reads ValueGeneratedOnAdd() off the model snapshot and adds
    /// IdentityByDefaultColumn by itself, so "no dual annotation is
    /// required". That probe ran GenerateCreateScript() (which renders from
    /// the model at run time); a MIGRATION is different — its DDL is fixed
    /// C# emitted at design time under whichever provider was active then
    /// (Sqlite, hence the INTEGER/TEXT column types above), and Npgsql
    /// re-renders none of it. Every other service's migrations already carry
    /// the annotation for this reason (UserService x2, VehicleService x7,
    /// CustomerService x6, SalesService x3, ReportingService x2). The comment
    /// in DbProviderSelector is corrected in this PR.
    ///
    /// These are NOT IF NOT EXISTS, so this baseline only runs against a
    /// database that does not yet have these tables. That holds everywhere it
    /// can meet: a brand-new Postgres instance is empty, and any existing
    /// NotificationService database was built by EnsureCreated() against a
    /// DEDICATED SQLite file (compose gave this service its own
    /// Data Source=/app/data/notifications.db before P2) carrying a schema
    /// this migration does not reconcile. Down is a no-op for the same
    /// reason — the pre-migration state is not this migration's output.
    /// </summary>
    public partial class NotificationBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    // Issue #92 (P2): TEXT here would create a real `text` column on
                    // the Postgres deploy, while the model renders this property as
                    // `timestamp with time zone` — the same model/database split the
                    // other five services carry and that their PostgresColumnTypes
                    // migrations repair. Stamping the real type now costs nothing
                    // (this baseline has never been applied anywhere) and saves a
                    // seventh repair migration later. NOT NULL, so no NULLIF guard.
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP WITH TIME ZONE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    // Issue #92 (P2): see the DeviceTokens.UpdatedAt note above.
                    // The boolean half matters most here — an INTEGER column here
                    // is what made NotificationService's preference writes fail on
                    // Postgres, mirroring the Users.IsActive 42804.
                    EmailNotifications = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    SmsNotifications = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    InAppNotifications = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    Orders = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    Deliveries = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    Payments = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    SystemAlerts = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    Promotions = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP WITH TIME ZONE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_Key_Token",
                table: "DeviceTokens",
                columns: new[] { "Key", "Token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_Key",
                table: "NotificationPreferences",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceTokens");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");
        }
    }
}
