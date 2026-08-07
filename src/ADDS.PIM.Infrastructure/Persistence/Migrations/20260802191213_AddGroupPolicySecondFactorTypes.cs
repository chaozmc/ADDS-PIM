using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupPolicySecondFactorTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AllowedSecondFactorTypes",
                table: "GroupPolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GroupPolicies_SecondFactor",
                table: "GroupPolicies",
                sql: "([RequiresSecondFactor] = 0 OR [AllowedSecondFactorTypes] IN (1, 2, 3)) AND [AllowedSecondFactorTypes] IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GroupPolicies_SecondFactor",
                table: "GroupPolicies");

            migrationBuilder.DropColumn(
                name: "AllowedSecondFactorTypes",
                table: "GroupPolicies");
        }
    }
}
