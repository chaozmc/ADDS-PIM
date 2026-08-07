using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationFindingDeactivationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeactivatedByObjectGuid",
                table: "DirectoryReconciliationFindings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedUtc",
                table: "DirectoryReconciliationFindings",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeactivatedByObjectGuid",
                table: "DirectoryReconciliationFindings");

            migrationBuilder.DropColumn(
                name: "DeactivatedUtc",
                table: "DirectoryReconciliationFindings");
        }
    }
}
