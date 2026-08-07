using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFido2ChallengesAndCredentialLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Fido2Credentials_PersonId",
                table: "Fido2Credentials");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "Fido2Credentials",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Fido2Challenges",
                columns: table => new
                {
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Challenge = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SatisfiedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fido2Challenges", x => x.ChallengeId);
                    table.CheckConstraint("CK_Fido2Challenges_State", "[Purpose] IN ('Registration', 'StepUp') AND [ExpiresUtc] > [CreatedUtc] AND ([ConsumedUtc] IS NULL OR ([SatisfiedBy] IN (1, 2) AND [ConsumedUtc] <= [ExpiresUtc]))");
                    table.ForeignKey(
                        name: "FK_Fido2Challenges_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_PersonId",
                table: "Fido2Credentials",
                column: "PersonId",
                filter: "[RevokedUtc] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Fido2Credentials_State",
                table: "Fido2Credentials",
                sql: "[SignatureCounter] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Challenges_PersonId_ExpiresUtc",
                table: "Fido2Challenges",
                columns: new[] { "PersonId", "ExpiresUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fido2Challenges");

            migrationBuilder.DropIndex(
                name: "IX_Fido2Credentials_PersonId",
                table: "Fido2Credentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Fido2Credentials_State",
                table: "Fido2Credentials");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "Fido2Credentials");

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_PersonId",
                table: "Fido2Credentials",
                column: "PersonId");
        }
    }
}
