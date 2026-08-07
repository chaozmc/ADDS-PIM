using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebSigningCertificateTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebSigningCertificates",
                columns: table => new
                {
                    WebSigningCertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Thumbprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PublicCertificateDer = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebSigningCertificates", x => x.WebSigningCertificateId);
                    table.CheckConstraint("CK_WebSigningCertificates_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebSigningCertificates_KeyId",
                table: "WebSigningCertificates",
                column: "KeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebSigningCertificates_Thumbprint",
                table: "WebSigningCertificates",
                column: "Thumbprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebSigningCertificates");
        }
    }
}
