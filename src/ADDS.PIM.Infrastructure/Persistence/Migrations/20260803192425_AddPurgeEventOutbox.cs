using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurgeEventOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurgeEventOutbox",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveryAttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailureCategory = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurgeEventOutbox", x => x.OutboxId);
                    table.CheckConstraint("CK_PurgeEventOutbox_DeliveryAttempts", "[DeliveryAttemptCount] >= 0");
                    table.CheckConstraint("CK_PurgeEventOutbox_EventId", "[EventId] BETWEEN 1 AND 65535");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurgeEventOutbox_CorrelationId",
                table: "PurgeEventOutbox",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_PurgeEventOutbox_DeliveredUtc_CreatedUtc",
                table: "PurgeEventOutbox",
                columns: new[] { "DeliveredUtc", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurgeEventOutbox");
        }
    }
}
