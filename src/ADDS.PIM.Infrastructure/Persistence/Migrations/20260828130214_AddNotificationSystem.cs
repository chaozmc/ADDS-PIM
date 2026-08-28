using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupNotificationRecipients",
                columns: table => new
                {
                    GroupNotificationRecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupNotificationRecipients", x => x.GroupNotificationRecipientId);
                    table.CheckConstraint("CK_GroupNotificationRecipients_EmailAddress", "LEN([EmailAddress]) > 0");
                    table.ForeignKey(
                        name: "FK_GroupNotificationRecipients_TargetGroups_TargetGroupId",
                        column: x => x.TargetGroupId,
                        principalTable: "TargetGroups",
                        principalColumn: "TargetGroupId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MailNotificationOutbox",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientAddresses = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveryAttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailureMessage = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailNotificationOutbox", x => x.OutboxId);
                    table.CheckConstraint("CK_MailNotificationOutbox_DeliveryAttempts", "[DeliveryAttemptCount] >= 0");
                    table.ForeignKey(
                        name: "FK_MailNotificationOutbox_MembershipRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "MembershipRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    NotificationTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.NotificationTemplateId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupNotificationRecipients_TargetGroupId_EmailAddress",
                table: "GroupNotificationRecipients",
                columns: new[] { "TargetGroupId", "EmailAddress" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MailNotificationOutbox_DeliveredUtc_CreatedUtc",
                table: "MailNotificationOutbox",
                columns: new[] { "DeliveredUtc", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MailNotificationOutbox_RequestId",
                table: "MailNotificationOutbox",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_TemplateKey",
                table: "NotificationTemplates",
                column: "TemplateKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupNotificationRecipients");

            migrationBuilder.DropTable(
                name: "MailNotificationOutbox");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");
        }
    }
}
