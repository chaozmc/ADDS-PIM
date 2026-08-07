using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSecurityLevelClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredSecurityLevel",
                table: "GroupPolicies");

            migrationBuilder.DropColumn(
                name: "RequiredSecurityLevel",
                table: "DirectEntitlements");

            migrationBuilder.RenameColumn(name: "SecurityLevel", table: "MfaTransactions", newName: "PolicyRequirementsSummary");
            migrationBuilder.RenameColumn(name: "SecurityLevel", table: "AuditEvents", newName: "PolicyRequirementsSummary");
            migrationBuilder.AlterColumn<string>(name: "PolicyRequirementsSummary", table: "MfaTransactions", type: "nvarchar(512)", maxLength: 512, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);
            migrationBuilder.AlterColumn<string>(name: "PolicyRequirementsSummary", table: "AuditEvents", type: "nvarchar(512)", maxLength: 512, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(name: "PolicyRequirementsSummary", table: "MfaTransactions", type: "nvarchar(128)", maxLength: 128, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(512)", oldMaxLength: 512);
            migrationBuilder.AlterColumn<string>(name: "PolicyRequirementsSummary", table: "AuditEvents", type: "nvarchar(128)", maxLength: 128, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(512)", oldMaxLength: 512);
            migrationBuilder.RenameColumn(name: "PolicyRequirementsSummary", table: "MfaTransactions", newName: "SecurityLevel");
            migrationBuilder.RenameColumn(name: "PolicyRequirementsSummary", table: "AuditEvents", newName: "SecurityLevel");

            migrationBuilder.AddColumn<string>(
                name: "RequiredSecurityLevel",
                table: "GroupPolicies",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequiredSecurityLevel",
                table: "DirectEntitlements",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

        }
    }
}
