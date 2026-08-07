using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationIdentityModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [dbo].[MembershipRequests])
                   OR EXISTS (SELECT 1 FROM [dbo].[AuditEvents])
                BEGIN
                    THROW 51000, 'Prototype membership-request or audit data cannot be safely mapped to the current identity tuple. Quarantine it before applying this migration.', 1;
                END
                """);

            migrationBuilder.DropIndex(
                name: "IX_MembershipRequests_SubjectId_CreatedUtc",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "AuditEvents");

            migrationBuilder.AddColumn<string>(
                name: "ActorAccountDisplayNameSnapshot",
                table: "MembershipRequests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ActorAccountId",
                table: "MembershipRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "EntitlementId",
                table: "MembershipRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "PersonDisplayNameSnapshot",
                table: "MembershipRequests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PersonId",
                table: "MembershipRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "TargetAccountDisplayNameSnapshot",
                table: "MembershipRequests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TargetAccountId",
                table: "MembershipRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "TargetGroupDisplayNameSnapshot",
                table: "MembershipRequests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ActorAccountDisplayNameSnapshot",
                table: "AuditEvents",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorAccountId",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonDisplayNameSnapshot",
                table: "AuditEvents",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonId",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetAccountDisplayNameSnapshot",
                table: "AuditEvents",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetAccountId",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetGroupDisplayNameSnapshot",
                table: "AuditEvents",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DirectoryScopes",
                columns: table => new
                {
                    DirectoryScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StableScopeIdentifier = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryScopes", x => x.DirectoryScopeId);
                    table.CheckConstraint("CK_DirectoryScopes_StableScopeIdentifier", "LEN([StableScopeIdentifier]) > 0");
                });

            migrationBuilder.CreateTable(
                name: "GroupPolicies",
                columns: table => new
                {
                    GroupPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinimumTtlSeconds = table.Column<long>(type: "bigint", nullable: false),
                    MaximumTtlSeconds = table.Column<long>(type: "bigint", nullable: false),
                    DefaultTtlSeconds = table.Column<long>(type: "bigint", nullable: false),
                    AllowedTtlStepSeconds = table.Column<long>(type: "bigint", nullable: false),
                    RequiredSecurityLevel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequiresSecondFactor = table.Column<bool>(type: "bit", nullable: false),
                    RequiresTicket = table.Column<bool>(type: "bit", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupPolicies", x => x.GroupPolicyId);
                    table.CheckConstraint("CK_GroupPolicies_Ttl", "[MinimumTtlSeconds] > 0 AND [MaximumTtlSeconds] >= [MinimumTtlSeconds] AND [DefaultTtlSeconds] BETWEEN [MinimumTtlSeconds] AND [MaximumTtlSeconds] AND [AllowedTtlStepSeconds] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.PersonId);
                    table.CheckConstraint("CK_Persons_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]");
                });

            migrationBuilder.CreateTable(
                name: "DirectoryAccounts",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DirectoryScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectSid = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SamAccountName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserPrincipalName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DistinguishedName = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DomainQualifiedName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsEnabledInDirectory = table.Column<bool>(type: "bit", nullable: false),
                    IsWithinAllowedScope = table.Column<bool>(type: "bit", nullable: false),
                    LastVerifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryAccounts", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_DirectoryAccounts_DirectoryScopes_DirectoryScopeId",
                        column: x => x.DirectoryScopeId,
                        principalTable: "DirectoryScopes",
                        principalColumn: "DirectoryScopeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TargetGroups",
                columns: table => new
                {
                    TargetGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DirectoryScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectSid = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SamAccountName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DistinguishedName = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DomainQualifiedName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    GroupPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabledForRequests = table.Column<bool>(type: "bit", nullable: false),
                    IsWithinAllowedScope = table.Column<bool>(type: "bit", nullable: false),
                    LastVerifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetGroups", x => x.TargetGroupId);
                    table.ForeignKey(
                        name: "FK_TargetGroups_DirectoryScopes_DirectoryScopeId",
                        column: x => x.DirectoryScopeId,
                        principalTable: "DirectoryScopes",
                        principalColumn: "DirectoryScopeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TargetGroups_GroupPolicies_GroupPolicyId",
                        column: x => x.GroupPolicyId,
                        principalTable: "GroupPolicies",
                        principalColumn: "GroupPolicyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonAccountLinks",
                columns: table => new
                {
                    PersonAccountLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MayAuthenticate = table.Column<bool>(type: "bit", nullable: false),
                    MayReceivePrivileges = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonAccountLinks", x => x.PersonAccountLinkId);
                    table.CheckConstraint("CK_PersonAccountLinks_Purpose", "[MayAuthenticate] = 1 OR [MayReceivePrivileges] = 1");
                    table.CheckConstraint("CK_PersonAccountLinks_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]");
                    table.ForeignKey(
                        name: "FK_PersonAccountLinks_DirectoryAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "DirectoryAccounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonAccountLinks_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirectEntitlements",
                columns: table => new
                {
                    EntitlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MinimumTtlSeconds = table.Column<long>(type: "bigint", nullable: true),
                    MaximumTtlSeconds = table.Column<long>(type: "bigint", nullable: true),
                    AllowedTtlStepSeconds = table.Column<long>(type: "bigint", nullable: true),
                    RequiredSecurityLevel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequiresSecondFactor = table.Column<bool>(type: "bit", nullable: true),
                    RequiresTicket = table.Column<bool>(type: "bit", nullable: true),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectEntitlements", x => x.EntitlementId);
                    table.CheckConstraint("CK_DirectEntitlements_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]");
                    table.ForeignKey(
                        name: "FK_DirectEntitlements_DirectoryAccounts_TargetAccountId",
                        column: x => x.TargetAccountId,
                        principalTable: "DirectoryAccounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectEntitlements_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectEntitlements_TargetGroups_TargetGroupId",
                        column: x => x.TargetGroupId,
                        principalTable: "TargetGroups",
                        principalColumn: "TargetGroupId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRequests_ActorAccountId",
                table: "MembershipRequests",
                column: "ActorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRequests_EntitlementId",
                table: "MembershipRequests",
                column: "EntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRequests_PersonId_CreatedUtc",
                table: "MembershipRequests",
                columns: new[] { "PersonId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRequests_TargetAccountId",
                table: "MembershipRequests",
                column: "TargetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRequests_TargetGroupId",
                table: "MembershipRequests",
                column: "TargetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectEntitlements_PersonId_TargetAccountId_TargetGroupId",
                table: "DirectEntitlements",
                columns: new[] { "PersonId", "TargetAccountId", "TargetGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectEntitlements_TargetAccountId",
                table: "DirectEntitlements",
                column: "TargetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectEntitlements_TargetGroupId",
                table: "DirectEntitlements",
                column: "TargetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryAccounts_DirectoryScopeId_ObjectGuid",
                table: "DirectoryAccounts",
                columns: new[] { "DirectoryScopeId", "ObjectGuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryScopes_StableScopeIdentifier",
                table: "DirectoryScopes",
                column: "StableScopeIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonAccountLinks_AccountId",
                table: "PersonAccountLinks",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonAccountLinks_PersonId",
                table: "PersonAccountLinks",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_TargetGroups_DirectoryScopeId_ObjectGuid",
                table: "TargetGroups",
                columns: new[] { "DirectoryScopeId", "ObjectGuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TargetGroups_GroupPolicyId",
                table: "TargetGroups",
                column: "GroupPolicyId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_DirectEntitlements_EntitlementId",
                table: "MembershipRequests",
                column: "EntitlementId",
                principalTable: "DirectEntitlements",
                principalColumn: "EntitlementId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_DirectoryAccounts_ActorAccountId",
                table: "MembershipRequests",
                column: "ActorAccountId",
                principalTable: "DirectoryAccounts",
                principalColumn: "AccountId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_DirectoryAccounts_TargetAccountId",
                table: "MembershipRequests",
                column: "TargetAccountId",
                principalTable: "DirectoryAccounts",
                principalColumn: "AccountId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_Persons_PersonId",
                table: "MembershipRequests",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "PersonId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_TargetGroups_TargetGroupId",
                table: "MembershipRequests",
                column: "TargetGroupId",
                principalTable: "TargetGroups",
                principalColumn: "TargetGroupId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_DirectEntitlements_EntitlementId",
                table: "MembershipRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_DirectoryAccounts_ActorAccountId",
                table: "MembershipRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_DirectoryAccounts_TargetAccountId",
                table: "MembershipRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_Persons_PersonId",
                table: "MembershipRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_TargetGroups_TargetGroupId",
                table: "MembershipRequests");

            migrationBuilder.DropTable(
                name: "DirectEntitlements");

            migrationBuilder.DropTable(
                name: "PersonAccountLinks");

            migrationBuilder.DropTable(
                name: "TargetGroups");

            migrationBuilder.DropTable(
                name: "DirectoryAccounts");

            migrationBuilder.DropTable(
                name: "Persons");

            migrationBuilder.DropTable(
                name: "GroupPolicies");

            migrationBuilder.DropTable(
                name: "DirectoryScopes");

            migrationBuilder.DropIndex(
                name: "IX_MembershipRequests_ActorAccountId",
                table: "MembershipRequests");

            migrationBuilder.DropIndex(
                name: "IX_MembershipRequests_EntitlementId",
                table: "MembershipRequests");

            migrationBuilder.DropIndex(
                name: "IX_MembershipRequests_PersonId_CreatedUtc",
                table: "MembershipRequests");

            migrationBuilder.DropIndex(
                name: "IX_MembershipRequests_TargetAccountId",
                table: "MembershipRequests");

            migrationBuilder.DropIndex(
                name: "IX_MembershipRequests_TargetGroupId",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "ActorAccountDisplayNameSnapshot",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "ActorAccountId",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "EntitlementId",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "PersonDisplayNameSnapshot",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "TargetAccountDisplayNameSnapshot",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "TargetAccountId",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "TargetGroupDisplayNameSnapshot",
                table: "MembershipRequests");

            migrationBuilder.DropColumn(
                name: "ActorAccountDisplayNameSnapshot",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "ActorAccountId",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "PersonDisplayNameSnapshot",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "TargetAccountDisplayNameSnapshot",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "TargetAccountId",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "TargetGroupDisplayNameSnapshot",
                table: "AuditEvents");

            migrationBuilder.AddColumn<string>(
                name: "SubjectId",
                table: "MembershipRequests",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubjectId",
                table: "AuditEvents",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRequests_SubjectId_CreatedUtc",
                table: "MembershipRequests",
                columns: new[] { "SubjectId", "CreatedUtc" });
        }
    }
}
