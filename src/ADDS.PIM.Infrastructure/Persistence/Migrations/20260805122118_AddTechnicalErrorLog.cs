using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicalErrorLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TechnicalErrorLogEntries",
                columns: table => new
                {
                    ErrorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ExceptionType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    SourceComponent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicalErrorLogEntries", x => x.ErrorId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalErrorLogEntries_CorrelationId",
                table: "TechnicalErrorLogEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalErrorLogEntries_OccurredUtc",
                table: "TechnicalErrorLogEntries",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalErrorLogEntries_RequestId",
                table: "TechnicalErrorLogEntries",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TechnicalErrorLogEntries");
        }
    }
}
