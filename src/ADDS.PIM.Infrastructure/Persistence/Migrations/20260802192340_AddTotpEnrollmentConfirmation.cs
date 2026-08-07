using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTotpEnrollmentConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TotpFactors_State",
                table: "TotpFactors");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedUtc",
                table: "TotpFactors",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnrollmentExpiresUtc",
                table: "TotpFactors",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddCheckConstraint(
                name: "CK_TotpFactors_State",
                table: "TotpFactors",
                sql: "[ConsecutiveFailedAttempts] >= 0 AND [EnrollmentExpiresUtc] > [EnrolledUtc] AND ([IsActive] = 0 OR [ConfirmedUtc] IS NOT NULL) AND ([RevokedUtc] IS NULL OR [IsActive] = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TotpFactors_State",
                table: "TotpFactors");

            migrationBuilder.DropColumn(
                name: "ConfirmedUtc",
                table: "TotpFactors");

            migrationBuilder.DropColumn(
                name: "EnrollmentExpiresUtc",
                table: "TotpFactors");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TotpFactors_State",
                table: "TotpFactors",
                sql: "[ConsecutiveFailedAttempts] >= 0 AND ([IsActive] = 1 OR [RevokedUtc] IS NOT NULL)");
        }
    }
}
