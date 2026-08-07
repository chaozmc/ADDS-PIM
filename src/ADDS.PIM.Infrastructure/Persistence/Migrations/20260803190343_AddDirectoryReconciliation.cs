using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectoryReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DirectoryReconciliationRuns",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DirectoryScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByObjectGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TechnicalClientId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceIpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureCategory = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryReconciliationRuns", x => x.RunId);
                    table.ForeignKey(
                        name: "FK_DirectoryReconciliationRuns_DirectoryScopes_DirectoryScopeId",
                        column: x => x.DirectoryScopeId,
                        principalTable: "DirectoryScopes",
                        principalColumn: "DirectoryScopeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirectoryReconciliationFindings",
                columns: table => new
                {
                    FindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<int>(type: "int", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DirectoryScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    DisplayNameSnapshot = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DetectedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryReconciliationFindings", x => x.FindingId);
                    table.ForeignKey(
                        name: "FK_DirectoryReconciliationFindings_DirectoryReconciliationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "DirectoryReconciliationRuns",
                        principalColumn: "RunId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectoryReconciliationFindings_DirectoryScopes_DirectoryScopeId",
                        column: x => x.DirectoryScopeId,
                        principalTable: "DirectoryScopes",
                        principalColumn: "DirectoryScopeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryReconciliationFindings_DirectoryScopeId",
                table: "DirectoryReconciliationFindings",
                column: "DirectoryScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryReconciliationFindings_IsResolved_DetectedUtc",
                table: "DirectoryReconciliationFindings",
                columns: new[] { "IsResolved", "DetectedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryReconciliationFindings_RunId_EntityType_EntityId",
                table: "DirectoryReconciliationFindings",
                columns: new[] { "RunId", "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryReconciliationRuns_DirectoryScopeId",
                table: "DirectoryReconciliationRuns",
                column: "DirectoryScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryReconciliationRuns_Status",
                table: "DirectoryReconciliationRuns",
                column: "Status",
                unique: true,
                filter: "[Status] IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectoryReconciliationFindings");

            migrationBuilder.DropTable(
                name: "DirectoryReconciliationRuns");
        }
    }
}
