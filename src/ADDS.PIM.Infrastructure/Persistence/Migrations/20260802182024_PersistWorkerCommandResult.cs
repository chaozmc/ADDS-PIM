using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistWorkerCommandResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultDomainController",
                table: "WorkerCommands",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultErrorCode",
                table: "WorkerCommands",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultKind",
                table: "WorkerCommands",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResultRemainingTtlSeconds",
                table: "WorkerCommands",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultDomainController",
                table: "WorkerCommands");

            migrationBuilder.DropColumn(
                name: "ResultErrorCode",
                table: "WorkerCommands");

            migrationBuilder.DropColumn(
                name: "ResultKind",
                table: "WorkerCommands");

            migrationBuilder.DropColumn(
                name: "ResultRemainingTtlSeconds",
                table: "WorkerCommands");
        }
    }
}
