using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMembershipRequestSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TechnicalClientId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceIpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceComponent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedTtlSeconds = table.Column<long>(type: "bigint", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FailureCategory = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthenticationMethod = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SecurityLevel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "MembershipRequests",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedTtlSeconds = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TicketReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipRequests", x => x.RequestId);
                });

            migrationBuilder.CreateTable(
                name: "MembershipRequestStatusHistory",
                columns: table => new
                {
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceComponent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipRequestStatusHistory", x => x.EntryId);
                    table.ForeignKey(
                        name: "FK_MembershipRequestStatusHistory_MembershipRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "MembershipRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_RequestId_OccurredUtc",
                table: "AuditEvents",
                columns: new[] { "RequestId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRequests_SubjectId_CreatedUtc",
                table: "MembershipRequests",
                columns: new[] { "SubjectId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRequestStatusHistory_RequestId_OccurredUtc",
                table: "MembershipRequestStatusHistory",
                columns: new[] { "RequestId", "OccurredUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "MembershipRequestStatusHistory");

            migrationBuilder.DropTable(
                name: "MembershipRequests");
        }
    }
}
