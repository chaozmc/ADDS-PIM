using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApproverRightAndGroupApprovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MayApprove",
                table: "PersonAccountLinks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GroupApprovers",
                columns: table => new
                {
                    GroupApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupApprovers", x => x.GroupApproverId);
                    table.CheckConstraint("CK_GroupApprovers_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]");
                    table.ForeignKey(
                        name: "FK_GroupApprovers_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupApprovers_TargetGroups_TargetGroupId",
                        column: x => x.TargetGroupId,
                        principalTable: "TargetGroups",
                        principalColumn: "TargetGroupId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PersonAccountLinks_MayApproveRequiresMayAuthenticate",
                table: "PersonAccountLinks",
                sql: "[MayApprove] = 0 OR [MayAuthenticate] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_GroupApprovers_PersonId",
                table: "GroupApprovers",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupApprovers_TargetGroupId",
                table: "GroupApprovers",
                column: "TargetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupApprovers_TargetGroupId_PersonId",
                table: "GroupApprovers",
                columns: new[] { "TargetGroupId", "PersonId" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupApprovers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PersonAccountLinks_MayApproveRequiresMayAuthenticate",
                table: "PersonAccountLinks");

            migrationBuilder.DropColumn(
                name: "MayApprove",
                table: "PersonAccountLinks");
        }
    }
}
