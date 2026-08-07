using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveAuthenticationAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PersonAccountLinks_PersonId",
                table: "PersonAccountLinks");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAccountLinks_PersonId",
                table: "PersonAccountLinks",
                column: "PersonId",
                unique: true,
                filter: "[MayAuthenticate] = 1 AND [IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PersonAccountLinks_PersonId",
                table: "PersonAccountLinks");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAccountLinks_PersonId",
                table: "PersonAccountLinks",
                column: "PersonId");
        }
    }
}
