using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequesterOutcomeNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationEmailOverride",
                table: "Persons",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequesterNotificationSettings",
                columns: table => new
                {
                    RequesterNotificationSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CcAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    BccAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequesterNotificationSettings", x => x.RequesterNotificationSettingsId);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Persons_NotificationEmailOverride",
                table: "Persons",
                sql: "[NotificationEmailOverride] IS NULL OR LEN([NotificationEmailOverride]) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequesterNotificationSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Persons_NotificationEmailOverride",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "NotificationEmailOverride",
                table: "Persons");
        }
    }
}
