using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UserService.Data;

#nullable disable

namespace UserService.Migrations
{
    /// <summary>
    /// BASELINE migration for the squashed history (issue #121, review round 2).
    ///
    /// This file replaces three migrations that were UNSAFE on a database
    /// shared with VehicleService, which is the whole point of issue #121:
    ///
    ///   20251122042647_InitialCreate     -- CreateTable("Dealers") + the
    ///                                      FK_Users_Dealers_DealerId
    ///                                      constraint + IX_Users_DealerId.
    ///   20260918081107_AlignDealersSchema -- 5 AlterColumn/AddColumn on
    ///                                      "Dealers" (harmless on its own, but
    ///                                      it only ever existed to patch the
    ///                                      shared table).
    ///   20260918123831_DropDealersTable  -- DropTable("Dealers"). This one is
    ///                                      the DATA-LOSS path: it is a no-op
    ///                                      against the Dealers table
    ///                                      VehicleService owns.
    ///
    /// WHY THE SQUASH WAS REQUIRED, NOT COSMETIC
    /// Migrate() replays every migration not yet recorded in
    /// __EFMigrationsHistory. VehicleService uses EnsureCreated(), which
    /// creates the schema + HasData seed but does NOT write that history
    /// table (probed). So whichever service boots second, the pair breaks:
    ///
    ///   VehicleService first -> "Dealers" already exists when InitialCreate
    ///     replays -> SqliteException 'table "Dealers" already exists' ->
    ///     userservice FAILS TO BOOT and crashloops (probed, scenario 1).
    ///   UserService first -> DropDealersTable drops VehicleService's table,
    ///     and EnsureCreated() on an existing database creates NOTHING (it
    ///     returns false rather than throwing, probed) -> every VehicleService
    ///     endpoint dies on "no such table" while /health stays green.
    ///
    /// Both orderings break. The fix is not "one service stops mapping the
    /// table": it is that BOTH services must use Migrate() with DISJOINT
    /// table sets, coordinated by __EFMigrationsHistory. This baseline makes
    /// UserService's side of that true -- a fresh database now gets exactly
    /// "Users" and "PasswordResetTokens" and never touches "Dealers". See
    /// VehicleService/Migrations/...Baseline for the other side.
    ///
    /// EXISTING DATABASES
    /// The migration id below is deliberately NEW, and the three old ids are
    /// gone, so no database can have this row yet. Migrate() therefore runs
    /// this file against every existing UserService database. Its Up body is
    /// idempotent under those conditions:
    ///   - a DB at the old InitialCreate has Users/PasswordResetTokens/Dealers
    ///     -> this creates nothing new (both tables already exist), and
    ///     Dealers + its FK/index are left in place untouched, NOT dropped.
    ///   - a DB at DropDealersTable has just Users/PasswordResetTokens ->
    ///     this is a straight no-op.
    /// Either way no user data is touched. The orphaned Dealers table and the
    /// FK_Users_Dealers_DealerId constraint, if present, remain
    /// VehicleService's problem to reconcile (it owns the table now), and its
    /// own baseline handles that. Nothing here DROPS anything.
    /// </summary>
    [DbContext(typeof(UserDbContext))]
    [Migration("20260918150000_Baseline")]
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deliberately does NOT create "Dealers" and does NOT add a
            // DealerId FK: VehicleService owns that table (issue #121).
            // User.DealerId stays a plain nullable int column with NO
            // constraint, so the two contexts never emit conflicting DDL
            // against the same object.
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                       .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DealerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                       .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_Token",
                table: "PasswordResetTokens",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
