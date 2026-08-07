using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiRequestReplayProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiRequestReplays",
                columns: table => new
                {
                    ReplayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CanonicalRequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IssuedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiRequestReplays", x => x.ReplayId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestReplays_Nonce",
                table: "ApiRequestReplays",
                column: "Nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestReplays_RequestId",
                table: "ApiRequestReplays",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiRequestReplays");
        }
    }
}
