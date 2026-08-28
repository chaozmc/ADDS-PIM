using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Tests.Administration;

public sealed class PersonAccountManagementUseCaseTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly AdministrationActor Actor = new(ScopeId, Guid.NewGuid());

    [Fact]
    public async Task CreatePersonAccountLink_RejectsEmptyPurposeBeforeDirectoryLookup()
    {
        var directory = new FakeDirectoryAccounts(); var store = new FakeStore(); var useCase = CreateUseCase(store, directory);
        var result = await useCase.CreatePersonAccountLinkAsync(new(Actor, Guid.NewGuid(), Guid.NewGuid(), false, false, false), AuditContext(), CancellationToken.None);
        Assert.Equal(AdministrationUpdateResult.Invalid, result); Assert.Equal(0, directory.ResolveAccountCalls); Assert.Null(store.CreateRequest);
    }

    [Fact]
    public async Task CreatePersonAccountLink_RejectsDisabledDirectoryAccount()
    {
        var directory = new FakeDirectoryAccounts { Account = Account(false) }; var store = new FakeStore(); var useCase = CreateUseCase(store, directory);
        var result = await useCase.CreatePersonAccountLinkAsync(new(Actor, Guid.NewGuid(), directory.Account.ObjectGuid, false, true, false), AuditContext(), CancellationToken.None);
        Assert.Equal(AdministrationUpdateResult.NotFound, result); Assert.Null(store.CreateRequest);
    }

    [Fact]
    public async Task CreatePersonAccountLink_UsesVerifiedGenericDirectoryAccount()
    {
        var directory = new FakeDirectoryAccounts { Account = Account(true) }; var store = new FakeStore(); var useCase = CreateUseCase(store, directory);
        var request = new CreatePersonAccountLinkRequest(Actor, Guid.NewGuid(), directory.Account.ObjectGuid, false, true, false);
        var result = await useCase.CreatePersonAccountLinkAsync(request, AuditContext(), CancellationToken.None);
        Assert.Equal(AdministrationUpdateResult.Updated, result); Assert.Same(request, store.CreateRequest); Assert.Equal(1, directory.ResolveAccountCalls);
    }

    [Fact]
    public async Task UpdatePersonAccountLinkPurposes_RejectsRemovingAllPurposes()
    {
        var store = new FakeStore(); var useCase = CreateUseCase(store, new FakeDirectoryAccounts());
        var result = await useCase.UpdatePersonAccountLinkPurposesAsync(new(Actor, Guid.NewGuid(), Guid.NewGuid(), false, false, false, "AQ=="), AuditContext(), CancellationToken.None);
        Assert.Equal(AdministrationUpdateResult.Invalid, result); Assert.Null(store.UpdatePurposesRequest);
    }

    [Fact]
    public async Task CreatePersonAccountLink_RejectsMayApproveWithoutMayAuthenticate()
    {
        var directory = new FakeDirectoryAccounts { Account = Account(true) }; var store = new FakeStore(); var useCase = CreateUseCase(store, directory);
        var result = await useCase.CreatePersonAccountLinkAsync(new(Actor, Guid.NewGuid(), directory.Account.ObjectGuid, false, true, true), AuditContext(), CancellationToken.None);
        Assert.Equal(AdministrationUpdateResult.Invalid, result); Assert.Null(store.CreateRequest);
    }

    [Fact]
    public async Task UpdatePersonAccountLinkPurposes_RejectsMayApproveWithoutMayAuthenticate()
    {
        var store = new FakeStore(); var useCase = CreateUseCase(store, new FakeDirectoryAccounts());
        var result = await useCase.UpdatePersonAccountLinkPurposesAsync(new(Actor, Guid.NewGuid(), Guid.NewGuid(), false, true, true, "AQ=="), AuditContext(), CancellationToken.None);
        Assert.Equal(AdministrationUpdateResult.Invalid, result); Assert.Null(store.UpdatePurposesRequest);
    }

    private static AdministrationUseCases CreateUseCase(FakeStore store, FakeDirectoryAccounts accounts)
        => new(store, null!, accounts, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"));
    private static AdministrationAuditContext AuditContext() => new(Guid.NewGuid(), "test", null);
    private static ResolvedDirectoryAccount Account(bool enabled) => new(Guid.NewGuid(), "account", "CN=account,DC=home,DC=local", "HOME\\account", "Account", null, null, "S-1-5-21", enabled);

    private sealed class FakeDirectoryAccounts : IDirectoryAccountResolver
    {
        public ResolvedDirectoryAccount Account { get; init; } = Account(true); public int ResolveAccountCalls { get; private set; }
        public Task<IReadOnlyList<ResolvedDirectoryAccount>> SearchPimUsersAsync(string searchTerm, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>([]);
        public Task<ResolvedDirectoryAccount?> ResolvePimUserAsync(Guid objectGuid, CancellationToken cancellationToken) => Task.FromResult<ResolvedDirectoryAccount?>(null);
        public Task<IReadOnlyList<ResolvedDirectoryAccount>> SearchAccountsAsync(string searchTerm, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>([Account]);
        public Task<ResolvedDirectoryAccount?> ResolveAccountAsync(Guid objectGuid, CancellationToken cancellationToken) { ResolveAccountCalls++; return Task.FromResult<ResolvedDirectoryAccount?>(Account.ObjectGuid == objectGuid ? Account : null); }
    }

    private sealed class FakeStore : IAdministrationDataStore
    {
        public CreatePersonAccountLinkRequest? CreateRequest { get; private set; } public UpdatePersonAccountLinkPurposesRequest? UpdatePurposesRequest { get; private set; }
        public Task<IReadOnlyList<ManagedPerson>> ListPersonsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedPerson>>([]);
        public Task<AdministrationUpdateResult> DeactivatePersonAsync(DeactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> ReactivatePersonAsync(ReactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> CreatePersonFromDirectoryAccountAsync(CreatePersonFromDirectoryAccountRequest request, ResolvedDirectoryAccount account, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<ManagedPersonDetail?> GetPersonDetailAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult<ManagedPersonDetail?>(null);
        public Task<AdministrationUpdateResult> CreatePersonAccountLinkAsync(CreatePersonAccountLinkRequest request, ResolvedDirectoryAccount account, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { CreateRequest=request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<AdministrationUpdateResult> UpdatePersonAccountLinkPurposesAsync(UpdatePersonAccountLinkPurposesRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { UpdatePurposesRequest=request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<AdministrationUpdateResult> DeactivatePersonAccountLinkAsync(DeactivatePersonAccountLinkRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> ReactivatePersonAccountLinkAsync(ReactivatePersonAccountLinkRequest request, ResolvedDirectoryAccount account, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<Guid?> GetPersonAccountLinkObjectGuidAsync(Guid personId, Guid personAccountLinkId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        public Task<IReadOnlyList<ManagedTargetGroup>> ListTargetGroupsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedTargetGroup>>([]);
        public Task<IReadOnlyList<ManagedTicketReferencePattern>> ListTicketReferencePatternsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedTicketReferencePattern>>([]);
        public Task<AdministrationUpdateResult> UpsertTicketReferencePatternAsync(UpsertTicketReferencePatternRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> DeleteTicketReferencePatternAsync(DeleteTicketReferencePatternRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<IReadOnlyList<ManagedDirectEntitlement>> ListDirectEntitlementsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedDirectEntitlement>>([]);
        public Task<IReadOnlyList<EntitlementSubject>> ListEligibleEntitlementSubjectsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EntitlementSubject>>([]);
        public Task<AdministrationUpdateResult> CreateDirectEntitlementAsync(CreateDirectEntitlementRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> UpdateDirectEntitlementValidityAsync(UpdateDirectEntitlementValidityRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> UpdateDirectEntitlementConstraintsAsync(UpdateDirectEntitlementConstraintsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> UpdateTargetGroupPolicyAsync(UpdateTargetGroupPolicyRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> DeactivateDirectEntitlementAsync(DeactivateDirectEntitlementRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> CreateTargetGroupAsync(CreateTargetGroupRequest request, ResolvedDirectoryGroup group, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> DeactivateTargetGroupAsync(DeactivateTargetGroupRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> ReactivateTargetGroupAsync(ReactivateTargetGroupRequest request, ResolvedDirectoryGroup group, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<Guid?> GetTargetGroupObjectGuidAsync(Guid targetGroupId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        public Task<IReadOnlyList<ManagedTotpFactor>> ListTotpFactorsAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedTotpFactor>>([]);
        public Task<AdministrationUpdateResult> RevokeTotpFactorAsync(RevokeTotpFactorRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<IReadOnlyList<ManagedFido2Credential>> ListFido2CredentialsAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedFido2Credential>>([]);
        public Task<AdministrationUpdateResult> RevokeFido2CredentialAsync(RevokeFido2CredentialRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<IReadOnlyList<ManagedGroupApprover>> ListGroupApproversAsync(Guid targetGroupId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedGroupApprover>>([]);
        public Task<AdministrationUpdateResult> AddGroupApproverAsync(AddGroupApproverRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> DeactivateGroupApproverAsync(DeactivateGroupApproverRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> UpdateGroupApproverNotificationPreferenceAsync(UpdateGroupApproverNotificationPreferenceRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<TotpProtectionCertificateStatus> GetTotpProtectionCertificateStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new TotpProtectionCertificateStatus(true, "THUMBPRINT", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365), true, 0, 0));
        public Task<AdministrationUpdateResult> RotateTotpProtectionCertificateAsync(RotateTotpProtectionCertificateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { RotateRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public RotateTotpProtectionCertificateRequest? RotateRequest { get; private set; }
        public Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken) => Task.FromResult(new CertificateOverview([]));
        public Task<MailSettingsSnapshot> GetMailSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new MailSettingsSnapshot(false, true, string.Empty, 0, string.Empty, null, false, SmtpTlsMode.None, string.Empty));
        public Task<MailNotificationOutboxStatus> GetMailNotificationOutboxStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new MailNotificationOutboxStatus(0, 0));
        public Task<AdministrationUpdateResult> PurgeMailNotificationOutboxAsync(PurgeMailNotificationOutboxRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> UpsertMailSettingsAsync(UpsertMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<MailSettingsTestResult> TestMailSettingsAsync(TestMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(new MailSettingsTestResult(true, null));
        public Task<RequesterNotificationSettingsSnapshot> GetRequesterNotificationSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new RequesterNotificationSettingsSnapshot(false, null, null, string.Empty));
        public Task<AdministrationUpdateResult> UpsertRequesterNotificationSettingsAsync(UpsertRequesterNotificationSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> SetPersonNotificationEmailOverrideAsync(SetPersonNotificationEmailOverrideRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<IReadOnlyList<ManagedGroupNotificationRecipient>> ListGroupNotificationRecipientsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedGroupNotificationRecipient>>([]);
        public Task<AdministrationUpdateResult> UpsertGroupNotificationRecipientAsync(UpsertGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> DeleteGroupNotificationRecipientAsync(DeleteGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<NotificationTemplateSnapshot> GetNotificationTemplateAsync(string templateKey, CancellationToken cancellationToken) => Task.FromResult(new NotificationTemplateSnapshot(templateKey, string.Empty, string.Empty, null));
        public Task<AdministrationUpdateResult> UpsertNotificationTemplateAsync(UpsertNotificationTemplateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
    }
}
