using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaFactorPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fido2Credentials",
                columns: table => new
                {
                    Fido2CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "varbinary(900)", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SignatureCounter = table.Column<long>(type: "bigint", nullable: false),
                    Aaguid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fido2Credentials", x => x.Fido2CredentialId);
                    table.ForeignKey(
                        name: "FK_Fido2Credentials_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfaTransactions",
                columns: table => new
                {
                    MfaTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedTtlSeconds = table.Column<long>(type: "bigint", nullable: false),
                    SecurityLevel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AllowedFactorTypes = table.Column<int>(type: "int", nullable: false),
                    TransactionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Fido2Challenge = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SatisfiedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaTransactions", x => x.MfaTransactionId);
                    table.CheckConstraint("CK_MfaTransactions_State", "[AllowedFactorTypes] IN (1, 2, 3) AND [ExpiresUtc] > [CreatedUtc] AND ([ConsumedUtc] IS NULL OR ([SatisfiedBy] IN (1, 2) AND [ConsumedUtc] <= [ExpiresUtc]))");
                    table.ForeignKey(
                        name: "FK_MfaTransactions_DirectoryAccounts_ActorAccountId",
                        column: x => x.ActorAccountId,
                        principalTable: "DirectoryAccounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfaTransactions_DirectoryAccounts_TargetAccountId",
                        column: x => x.TargetAccountId,
                        principalTable: "DirectoryAccounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfaTransactions_MembershipRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "MembershipRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfaTransactions_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfaTransactions_TargetGroups_TargetGroupId",
                        column: x => x.TargetGroupId,
                        principalTable: "TargetGroups",
                        principalColumn: "TargetGroupId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TotpFactors",
                columns: table => new
                {
                    TotpFactorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncryptedSecret = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ProtectionKeyId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EnrolledUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastUsedTimeStep = table.Column<long>(type: "bigint", nullable: true),
                    ConsecutiveFailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotpFactors", x => x.TotpFactorId);
                    table.CheckConstraint("CK_TotpFactors_State", "[ConsecutiveFailedAttempts] >= 0 AND ([IsActive] = 1 OR [RevokedUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TotpFactors_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TotpUsedTimeSteps",
                columns: table => new
                {
                    TotpFactorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeStep = table.Column<long>(type: "bigint", nullable: false),
                    UsedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MfaTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotpUsedTimeSteps", x => new { x.TotpFactorId, x.TimeStep });
                    table.ForeignKey(
                        name: "FK_TotpUsedTimeSteps_MfaTransactions_MfaTransactionId",
                        column: x => x.MfaTransactionId,
                        principalTable: "MfaTransactions",
                        principalColumn: "MfaTransactionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TotpUsedTimeSteps_TotpFactors_TotpFactorId",
                        column: x => x.TotpFactorId,
                        principalTable: "TotpFactors",
                        principalColumn: "TotpFactorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_CredentialId",
                table: "Fido2Credentials",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_PersonId",
                table: "Fido2Credentials",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaTransactions_ActorAccountId",
                table: "MfaTransactions",
                column: "ActorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaTransactions_PersonId_ExpiresUtc",
                table: "MfaTransactions",
                columns: new[] { "PersonId", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MfaTransactions_RequestId",
                table: "MfaTransactions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfaTransactions_TargetAccountId",
                table: "MfaTransactions",
                column: "TargetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaTransactions_TargetGroupId",
                table: "MfaTransactions",
                column: "TargetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TotpFactors_PersonId",
                table: "TotpFactors",
                column: "PersonId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TotpUsedTimeSteps_MfaTransactionId",
                table: "TotpUsedTimeSteps",
                column: "MfaTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fido2Credentials");

            migrationBuilder.DropTable(
                name: "TotpUsedTimeSteps");

            migrationBuilder.DropTable(
                name: "MfaTransactions");

            migrationBuilder.DropTable(
                name: "TotpFactors");
        }
    }
}
