using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserService.Migrations
{
    /// <inheritdoc />
    public partial class DropDealersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Dealers_DealerId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Dealers");

            migrationBuilder.DropIndex(
                name: "IX_Users_DealerId",
                table: "Users");
        }

        /// <inheritdoc />
        /// Down is intentionally a NO-OP. Recreating "Dealers" here would
        /// duplicate the table VehicleService now owns and silently re-create
        /// the exact collision this migration removes (Migrate vs EnsureCreated
        /// both claiming the whole database). Reverting UserService to a
        /// dealer-owning context requires restoring the DbSet AND deleting this
        /// migration by hand -- do not do it by running Down.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
