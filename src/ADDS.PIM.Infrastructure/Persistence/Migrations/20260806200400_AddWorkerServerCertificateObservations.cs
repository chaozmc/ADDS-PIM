using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerServerCertificateObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkerServerCertificateObservations",
                columns: table => new
                {
                    ObservationId = table.Column<int>(type: "int", nullable: false),
                    Thumbprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NotBeforeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NotAfterUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WasAccepted = table.Column<bool>(type: "bit", nullable: false),
                    ObservedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerServerCertificateObservations", x => x.ObservationId);
                    table.CheckConstraint("CK_WorkerServerCertificateObservations_Validity", "[NotAfterUtc] > [NotBeforeUtc]");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerServerCertificateObservations");
        }
    }
}
