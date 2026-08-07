using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class PimDbContext(DbContextOptions<PimDbContext> options) : DbContext(options)
{
    public DbSet<DirectoryScopeEntity> DirectoryScopes => Set<DirectoryScopeEntity>();
    public DbSet<PersonEntity> Persons => Set<PersonEntity>();
    public DbSet<DirectoryAccountEntity> DirectoryAccounts => Set<DirectoryAccountEntity>();
    public DbSet<PersonAccountLinkEntity> PersonAccountLinks => Set<PersonAccountLinkEntity>();
    public DbSet<GroupPolicyEntity> GroupPolicies => Set<GroupPolicyEntity>();
    public DbSet<TicketReferencePatternEntity> TicketReferencePatterns => Set<TicketReferencePatternEntity>();
    public DbSet<TargetGroupEntity> TargetGroups => Set<TargetGroupEntity>();
    public DbSet<DirectEntitlementEntity> DirectEntitlements => Set<DirectEntitlementEntity>();
    public DbSet<GroupApproverEntity> GroupApprovers => Set<GroupApproverEntity>();
    public DbSet<MembershipRequestEntity> MembershipRequests => Set<MembershipRequestEntity>();
    public DbSet<MembershipRequestStatusHistoryEntity> MembershipRequestStatusHistory => Set<MembershipRequestStatusHistoryEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<WorkerCommandEntity> WorkerCommands => Set<WorkerCommandEntity>();
    public DbSet<ApiRequestReplayEntity> ApiRequestReplays => Set<ApiRequestReplayEntity>();
    public DbSet<WebSigningCertificateEntity> WebSigningCertificates => Set<WebSigningCertificateEntity>();
    public DbSet<TotpFactorEntity> TotpFactors => Set<TotpFactorEntity>();
    public DbSet<TotpUsedTimeStepEntity> TotpUsedTimeSteps => Set<TotpUsedTimeStepEntity>();
    public DbSet<Fido2CredentialEntity> Fido2Credentials => Set<Fido2CredentialEntity>();
    public DbSet<MfaTransactionEntity> MfaTransactions => Set<MfaTransactionEntity>();
    public DbSet<Fido2ChallengeEntity> Fido2Challenges => Set<Fido2ChallengeEntity>();
    public DbSet<DirectoryReconciliationRunEntity> DirectoryReconciliationRuns => Set<DirectoryReconciliationRunEntity>();
    public DbSet<DirectoryReconciliationFindingEntity> DirectoryReconciliationFindings => Set<DirectoryReconciliationFindingEntity>();
    public DbSet<PurgeEventOutboxEntity> PurgeEventOutbox => Set<PurgeEventOutboxEntity>();
    public DbSet<TechnicalErrorLogEntryEntity> TechnicalErrorLogEntries => Set<TechnicalErrorLogEntryEntity>();
    public DbSet<WorkerServerCertificateObservationEntity> WorkerServerCertificateObservations => Set<WorkerServerCertificateObservationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAuthorizationModel(modelBuilder);
        ConfigureRequestModel(modelBuilder);
        ConfigureDirectoryReconciliationModel(modelBuilder);
        ConfigurePurgeEventOutboxModel(modelBuilder);
        ConfigureDiagnosticsModel(modelBuilder);
    }

    private static void ConfigureDiagnosticsModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TechnicalErrorLogEntryEntity>(entity =>
        {
            entity.ToTable("TechnicalErrorLogEntries");
            entity.HasKey(x => x.ErrorId);
            entity.Property(x => x.HttpMethod).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Path).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ExceptionType).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.StackTrace).HasMaxLength(8000);
            entity.Property(x => x.SourceComponent).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.OccurredUtc);
            entity.HasIndex(x => x.RequestId);
            entity.HasIndex(x => x.CorrelationId);
        });
    }

    private static void ConfigurePurgeEventOutboxModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurgeEventOutboxEntity>(entity =>
        {
            entity.ToTable("PurgeEventOutbox", table =>
            {
                table.HasCheckConstraint("CK_PurgeEventOutbox_EventId", "[EventId] BETWEEN 1 AND 65535");
                table.HasCheckConstraint("CK_PurgeEventOutbox_DeliveryAttempts", "[DeliveryAttemptCount] >= 0");
            });
            entity.HasKey(x => x.OutboxId);
            entity.Property(x => x.EventType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Payload).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.LastFailureCategory).HasMaxLength(64);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.DeliveredUtc, x.CreatedUtc });
            entity.HasIndex(x => x.CorrelationId);
        });
    }

    private static void ConfigureAuthorizationModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DirectoryScopeEntity>(entity =>
        {
            entity.ToTable("DirectoryScopes", table => table.HasCheckConstraint("CK_DirectoryScopes_StableScopeIdentifier", "LEN([StableScopeIdentifier]) > 0"));
            entity.HasKey(x => x.DirectoryScopeId);
            entity.Property(x => x.StableScopeIdentifier).HasMaxLength(256).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.StableScopeIdentifier).IsUnique();
        });
        modelBuilder.Entity<PersonEntity>(entity =>
        {
            entity.ToTable("Persons", table => table.HasCheckConstraint("CK_Persons_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]"));
            entity.HasKey(x => x.PersonId); entity.Property(x => x.DisplayName).HasMaxLength(256).IsRequired(); entity.Property(x => x.ExternalReference).HasMaxLength(256); entity.Property(x => x.RowVersion).IsRowVersion();
        });
        modelBuilder.Entity<DirectoryAccountEntity>(entity =>
        {
            entity.ToTable("DirectoryAccounts"); entity.HasKey(x => x.AccountId);
            entity.Property(x => x.ObjectSid).HasMaxLength(256); entity.Property(x => x.SamAccountName).HasMaxLength(256).IsRequired(); entity.Property(x => x.UserPrincipalName).HasMaxLength(512); entity.Property(x => x.EmailAddress).HasMaxLength(512); entity.Property(x => x.DistinguishedName).HasMaxLength(2048).IsRequired(); entity.Property(x => x.DomainQualifiedName).HasMaxLength(512).IsRequired(); entity.Property(x => x.DisplayName).HasMaxLength(512).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.DirectoryScopeId, x.ObjectGuid }).IsUnique();
            entity.HasOne<DirectoryScopeEntity>().WithMany().HasForeignKey(x => x.DirectoryScopeId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PersonAccountLinkEntity>(entity =>
        {
            entity.ToTable("PersonAccountLinks", table => { table.HasCheckConstraint("CK_PersonAccountLinks_Purpose", "[MayAuthenticate] = 1 OR [MayReceivePrivileges] = 1"); table.HasCheckConstraint("CK_PersonAccountLinks_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]"); table.HasCheckConstraint("CK_PersonAccountLinks_MayApproveRequiresMayAuthenticate", "[MayApprove] = 0 OR [MayAuthenticate] = 1"); });
            entity.HasKey(x => x.PersonAccountLinkId); entity.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired(); entity.Property(x => x.ModifiedBy).HasMaxLength(256).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.AccountId).IsUnique();
            entity.HasIndex(x => x.PersonId).IsUnique().HasFilter("[MayAuthenticate] = 1 AND [IsActive] = 1");
            entity.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectoryAccountEntity>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<GroupPolicyEntity>(entity =>
        {
            entity.ToTable("GroupPolicies", table =>
            {
                table.HasCheckConstraint("CK_GroupPolicies_Ttl", "[MinimumTtlSeconds] > 0 AND [MaximumTtlSeconds] >= [MinimumTtlSeconds] AND [DefaultTtlSeconds] BETWEEN [MinimumTtlSeconds] AND [MaximumTtlSeconds] AND [AllowedTtlStepSeconds] > 0");
                table.HasCheckConstraint("CK_GroupPolicies_SecondFactor", "([RequiresSecondFactor] = 0 OR [AllowedSecondFactorTypes] IN (1, 2, 3)) AND [AllowedSecondFactorTypes] IN (0, 1, 2, 3)");
            });
            entity.HasKey(x => x.GroupPolicyId); entity.Property(x => x.RowVersion).IsRowVersion();
        });
        modelBuilder.Entity<TicketReferencePatternEntity>(entity =>
        {
            entity.ToTable("TicketReferencePatterns", table => table.HasCheckConstraint("CK_TicketReferencePatterns_Label", "LEN([Label]) > 0"));
            entity.HasKey(x => x.TicketReferencePatternId);
            entity.Property(x => x.Label).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Expression).HasMaxLength(512).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.GroupPolicyId, x.Label }).IsUnique();
            entity.HasOne<GroupPolicyEntity>().WithMany().HasForeignKey(x => x.GroupPolicyId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TargetGroupEntity>(entity =>
        {
            entity.ToTable("TargetGroups"); entity.HasKey(x => x.TargetGroupId); entity.Property(x => x.ObjectSid).HasMaxLength(256); entity.Property(x => x.SamAccountName).HasMaxLength(256).IsRequired(); entity.Property(x => x.DistinguishedName).HasMaxLength(2048).IsRequired(); entity.Property(x => x.DomainQualifiedName).HasMaxLength(512).IsRequired(); entity.Property(x => x.DisplayName).HasMaxLength(512).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.DirectoryScopeId, x.ObjectGuid }).IsUnique(); entity.HasIndex(x => x.GroupPolicyId).IsUnique();
            entity.HasOne<DirectoryScopeEntity>().WithMany().HasForeignKey(x => x.DirectoryScopeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GroupPolicyEntity>().WithMany().HasForeignKey(x => x.GroupPolicyId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DirectEntitlementEntity>(entity =>
        {
            entity.ToTable("DirectEntitlements", table => table.HasCheckConstraint("CK_DirectEntitlements_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]"));
            entity.HasKey(x => x.EntitlementId); entity.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired(); entity.Property(x => x.ModifiedBy).HasMaxLength(256).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.PersonId, x.TargetAccountId, x.TargetGroupId });
            entity.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectoryAccountEntity>().WithMany().HasForeignKey(x => x.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TargetGroupEntity>().WithMany().HasForeignKey(x => x.TargetGroupId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<GroupApproverEntity>(entity =>
        {
            entity.ToTable("GroupApprovers", table => table.HasCheckConstraint("CK_GroupApprovers_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]"));
            entity.HasKey(x => x.GroupApproverId); entity.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired(); entity.Property(x => x.ModifiedBy).HasMaxLength(256).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.TargetGroupId);
            entity.HasIndex(x => new { x.TargetGroupId, x.PersonId }).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasOne<TargetGroupEntity>().WithMany().HasForeignKey(x => x.TargetGroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureRequestModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiRequestReplayEntity>(entity =>
        {
            entity.ToTable("ApiRequestReplays"); entity.HasKey(x => x.ReplayId);
            entity.Property(x => x.KeyId).HasMaxLength(128).IsRequired(); entity.Property(x => x.Nonce).HasMaxLength(128).IsRequired(); entity.Property(x => x.CanonicalRequestHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.RequestId).IsUnique(); entity.HasIndex(x => x.Nonce).IsUnique();
        });
        modelBuilder.Entity<WebSigningCertificateEntity>(entity =>
        {
            entity.ToTable("WebSigningCertificates", table => table.HasCheckConstraint("CK_WebSigningCertificates_Validity", "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]"));
            entity.HasKey(x => x.WebSigningCertificateId); entity.Property(x => x.KeyId).HasMaxLength(128).IsRequired(); entity.Property(x => x.Thumbprint).HasMaxLength(128).IsRequired(); entity.Property(x => x.Purpose).HasMaxLength(64).IsRequired(); entity.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.KeyId).IsUnique(); entity.HasIndex(x => x.Thumbprint).IsUnique();
        });
        modelBuilder.Entity<WorkerServerCertificateObservationEntity>(entity =>
        {
            entity.ToTable("WorkerServerCertificateObservations", table => table.HasCheckConstraint("CK_WorkerServerCertificateObservations_Validity", "[NotAfterUtc] > [NotBeforeUtc]"));
            entity.HasKey(x => x.ObservationId); entity.Property(x => x.ObservationId).ValueGeneratedNever(); entity.Property(x => x.Thumbprint).HasMaxLength(128).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
        });
        modelBuilder.Entity<TotpFactorEntity>(entity =>
        {
            entity.ToTable("TotpFactors", table => table.HasCheckConstraint("CK_TotpFactors_State", "[ConsecutiveFailedAttempts] >= 0 AND [EnrollmentExpiresUtc] > [EnrolledUtc] AND ([IsActive] = 0 OR [ConfirmedUtc] IS NOT NULL) AND ([RevokedUtc] IS NULL OR [IsActive] = 0)"));
            entity.HasKey(x => x.TotpFactorId); entity.Property(x => x.EncryptedSecret).IsRequired(); entity.Property(x => x.ProtectionKeyId).HasMaxLength(256).IsRequired(); entity.Property(x => x.RevokedBy).HasMaxLength(256); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.PersonId).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TotpUsedTimeStepEntity>(entity =>
        {
            entity.ToTable("TotpUsedTimeSteps"); entity.HasKey(x => new { x.TotpFactorId, x.TimeStep });
            entity.HasOne<TotpFactorEntity>().WithMany().HasForeignKey(x => x.TotpFactorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MfaTransactionEntity>().WithMany().HasForeignKey(x => x.MfaTransactionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Fido2CredentialEntity>(entity =>
        {
            entity.ToTable("Fido2Credentials", table => table.HasCheckConstraint("CK_Fido2Credentials_State", "[SignatureCounter] >= 0"));
            entity.HasKey(x => x.Fido2CredentialId); entity.Property(x => x.CredentialId).IsRequired(); entity.Property(x => x.PublicKey).IsRequired(); entity.Property(x => x.Aaguid).HasMaxLength(64); entity.Property(x => x.Label).HasMaxLength(128); entity.Property(x => x.RevokedBy).HasMaxLength(256); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.CredentialId).IsUnique(); entity.HasIndex(x => x.PersonId).HasFilter("[RevokedUtc] IS NULL");
            entity.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Fido2ChallengeEntity>(entity =>
        {
            entity.ToTable("Fido2Challenges", table => table.HasCheckConstraint("CK_Fido2Challenges_State", "[Purpose] IN ('Registration', 'StepUp') AND [ExpiresUtc] > [CreatedUtc] AND ([ConsumedUtc] IS NULL OR ([SatisfiedBy] IN (1, 2) AND [ConsumedUtc] <= [ExpiresUtc]))"));
            entity.HasKey(x => x.ChallengeId); entity.Property(x => x.Purpose).HasMaxLength(32).IsRequired(); entity.Property(x => x.Challenge).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.PersonId, x.ExpiresUtc });
            entity.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MfaTransactionEntity>(entity =>
        {
            entity.ToTable("MfaTransactions", table => table.HasCheckConstraint("CK_MfaTransactions_State", "[AllowedFactorTypes] IN (1, 2, 3) AND [ExpiresUtc] > [CreatedUtc] AND ([ConsumedUtc] IS NULL OR ([SatisfiedBy] IN (1, 2) AND [ConsumedUtc] <= [ExpiresUtc]))"));
            entity.HasKey(x => x.MfaTransactionId); entity.Property(x => x.PolicyRequirementsSummary).HasMaxLength(512).IsRequired(); entity.Property(x => x.TransactionHash).HasMaxLength(64).IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.RequestId).IsUnique(); entity.HasIndex(x => new { x.PersonId, x.ExpiresUtc });
            entity.HasOne<MembershipRequestEntity>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectoryAccountEntity>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectoryAccountEntity>().WithMany().HasForeignKey(x => x.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TargetGroupEntity>().WithMany().HasForeignKey(x => x.TargetGroupId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MembershipRequestEntity>(entity =>
        {
            entity.ToTable("MembershipRequests"); entity.HasKey(x => x.RequestId); entity.Property(x => x.Reason).IsRequired(); entity.Property(x => x.PersonDisplayNameSnapshot).HasMaxLength(512).IsRequired(); entity.Property(x => x.ActorAccountDisplayNameSnapshot).HasMaxLength(512).IsRequired(); entity.Property(x => x.TargetAccountDisplayNameSnapshot).HasMaxLength(512).IsRequired(); entity.Property(x => x.TargetGroupDisplayNameSnapshot).HasMaxLength(512).IsRequired(); entity.Property(x => x.Status).HasConversion<int>().IsRequired(); entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.PersonId, x.CreatedUtc });
            entity.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectoryAccountEntity>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectoryAccountEntity>().WithMany().HasForeignKey(x => x.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TargetGroupEntity>().WithMany().HasForeignKey(x => x.TargetGroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectEntitlementEntity>().WithMany().HasForeignKey(x => x.EntitlementId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MembershipRequestStatusHistoryEntity>(entity =>
        {
            entity.ToTable("MembershipRequestStatusHistory"); entity.HasKey(x => x.EntryId); entity.Property(x => x.ActorId).HasMaxLength(256).IsRequired(); entity.Property(x => x.SourceComponent).HasMaxLength(64).IsRequired(); entity.Property(x => x.Reason).HasMaxLength(512).IsRequired(); entity.Property(x => x.NewStatus).HasConversion<int>().IsRequired(); entity.Property(x => x.PreviousStatus).HasConversion<int>(); entity.HasIndex(x => new { x.RequestId, x.OccurredUtc }); entity.HasOne<MembershipRequestEntity>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.ToTable("AuditEvents"); entity.HasKey(x => x.EventId); entity.Property(x => x.EventType).HasMaxLength(128).IsRequired(); entity.Property(x => x.PersonDisplayNameSnapshot).HasMaxLength(512); entity.Property(x => x.ActorAccountDisplayNameSnapshot).HasMaxLength(512); entity.Property(x => x.TargetAccountDisplayNameSnapshot).HasMaxLength(512); entity.Property(x => x.TargetGroupDisplayNameSnapshot).HasMaxLength(512); entity.Property(x => x.FrontendClientId).HasMaxLength(256).IsRequired(); entity.Property(x => x.SourceComponent).HasMaxLength(64).IsRequired(); entity.Property(x => x.Result).HasMaxLength(64).IsRequired(); entity.Property(x => x.AuthenticationMethod).HasMaxLength(128).IsRequired(); entity.Property(x => x.PolicyRequirementsSummary).HasMaxLength(512).IsRequired(); entity.HasIndex(x => new { x.RequestId, x.OccurredUtc });
        });
        modelBuilder.Entity<WorkerCommandEntity>(entity =>
        {
            entity.ToTable("WorkerCommands");
            entity.HasKey(x => x.CommandId);
            entity.Property(x => x.Nonce).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CommandHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CallerCertificateThumbprint).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.ResultKind).HasMaxLength(64);
            entity.Property(x => x.ResultDomainController).HasMaxLength(512);
            entity.Property(x => x.ResultErrorCode).HasMaxLength(128);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => x.Nonce).IsUnique();
        });
    }

    private static void ConfigureDirectoryReconciliationModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DirectoryReconciliationRunEntity>(entity =>
        {
            entity.ToTable("DirectoryReconciliationRuns"); entity.HasKey(x => x.RunId);
            entity.Property(x => x.FrontendClientId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.SourceIpAddress).HasMaxLength(64);
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.FailureCategory).HasMaxLength(64);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.Status).HasFilter("[Status] IN (0, 1)").IsUnique();
            entity.HasOne<DirectoryScopeEntity>().WithMany().HasForeignKey(x => x.DirectoryScopeId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DirectoryReconciliationFindingEntity>(entity =>
        {
            entity.ToTable("DirectoryReconciliationFindings"); entity.HasKey(x => x.FindingId);
            entity.Property(x => x.EntityType).HasConversion<int>().IsRequired();
            entity.Property(x => x.Reason).HasConversion<int>().IsRequired();
            entity.Property(x => x.DisplayNameSnapshot).HasMaxLength(512).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.RunId, x.EntityType, x.EntityId }).IsUnique();
            entity.HasIndex(x => new { x.IsResolved, x.DetectedUtc });
            entity.HasOne<DirectoryReconciliationRunEntity>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectoryScopeEntity>().WithMany().HasForeignKey(x => x.DirectoryScopeId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
