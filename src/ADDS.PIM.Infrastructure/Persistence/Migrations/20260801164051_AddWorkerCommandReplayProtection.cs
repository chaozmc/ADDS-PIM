using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerCommandReplayProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkerCommands",
                columns: table => new
                {
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CallerCertificateThumbprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DirectoryScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetAccountObjectGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetGroupObjectGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedTtlSeconds = table.Column<long>(type: "bigint", nullable: false),
                    IssuedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerCommands", x => x.CommandId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerCommands_Nonce",
                table: "WorkerCommands",
                column: "Nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkerCommands_RequestId",
                table: "WorkerCommands",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerCommands");
        }
    }
}
