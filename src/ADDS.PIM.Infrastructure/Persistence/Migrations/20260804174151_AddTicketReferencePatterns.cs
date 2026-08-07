using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketReferencePatterns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketReferencePatterns",
                columns: table => new
                {
                    TicketReferencePatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Expression = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketReferencePatterns", x => x.TicketReferencePatternId);
                    table.CheckConstraint("CK_TicketReferencePatterns_Label", "LEN([Label]) > 0");
                    table.ForeignKey(
                        name: "FK_TicketReferencePatterns_GroupPolicies_GroupPolicyId",
                        column: x => x.GroupPolicyId,
                        principalTable: "GroupPolicies",
                        principalColumn: "GroupPolicyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketReferencePatterns_GroupPolicyId_Label",
                table: "TicketReferencePatterns",
                columns: new[] { "GroupPolicyId", "Label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketReferencePatterns");
        }
    }
}
