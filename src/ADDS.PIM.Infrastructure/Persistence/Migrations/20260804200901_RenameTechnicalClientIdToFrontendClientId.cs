using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameTechnicalClientIdToFrontendClientId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TechnicalClientId",
                table: "DirectoryReconciliationRuns",
                newName: "FrontendClientId");

            migrationBuilder.RenameColumn(
                name: "TechnicalClientId",
                table: "AuditEvents",
                newName: "FrontendClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FrontendClientId",
                table: "DirectoryReconciliationRuns",
                newName: "TechnicalClientId");

            migrationBuilder.RenameColumn(
                name: "FrontendClientId",
                table: "AuditEvents",
                newName: "TechnicalClientId");
        }
    }
}
