using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupNotificationRecipientTypeAndOutboxHeaderSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RecipientAddresses",
                table: "MailNotificationOutbox",
                newName: "ToAddresses");

            migrationBuilder.AddColumn<string>(
                name: "BccAddresses",
                table: "MailNotificationOutbox",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CcAddresses",
                table: "MailNotificationOutbox",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RecipientType",
                table: "GroupNotificationRecipients",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GroupNotificationRecipients_RecipientType",
                table: "GroupNotificationRecipients",
                sql: "[RecipientType] IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GroupNotificationRecipients_RecipientType",
                table: "GroupNotificationRecipients");

            migrationBuilder.DropColumn(
                name: "BccAddresses",
                table: "MailNotificationOutbox");

            migrationBuilder.DropColumn(
                name: "CcAddresses",
                table: "MailNotificationOutbox");

            migrationBuilder.DropColumn(
                name: "RecipientType",
                table: "GroupNotificationRecipients");

            migrationBuilder.RenameColumn(
                name: "ToAddresses",
                table: "MailNotificationOutbox",
                newName: "RecipientAddresses");
        }
    }
}
