using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Tests.Administration;

/// <summary>Validation performed by <see cref="AdministrationUseCases.UpsertMailSettingsAsync"/> and
/// <see cref="AdministrationUseCases.TestMailSettingsAsync"/> before the store is ever invoked.</summary>
public sealed class MailSettingsUseCaseTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly AdministrationActor Actor = new(ScopeId, Guid.NewGuid());

    [Fact]
    public async Task Upsert_AcceptsValidRequestAndDelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertMailSettingsRequest(Actor, true, "smtp.example.org", 587, "pim@example.org", "user", "secret", false, SmtpTlsMode.Explicit, null);

        var result = await useCase.UpsertMailSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.UpsertRequest);
    }

    [Fact]
    public async Task Upsert_RejectsActorOutsideConfiguredDirectoryScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var foreignActor = new AdministrationActor(Guid.NewGuid(), Guid.NewGuid());
        var request = new UpsertMailSettingsRequest(foreignActor, true, "smtp.example.org", 587, "pim@example.org", null, null, false, SmtpTlsMode.None, null);

        var result = await useCase.UpsertMailSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRequest);
    }

    [Fact]
    public async Task Upsert_RejectsBlankHost()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertMailSettingsRequest(Actor, true, "  ", 587, "pim@example.org", null, null, false, SmtpTlsMode.None, null);

        var result = await useCase.UpsertMailSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task Upsert_RejectsPortOutOfRange(int port)
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertMailSettingsRequest(Actor, true, "smtp.example.org", port, "pim@example.org", null, null, false, SmtpTlsMode.None, null);

        var result = await useCase.UpsertMailSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRequest);
    }

    [Fact]
    public async Task Upsert_RejectsSenderAddressWithoutAtSign()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertMailSettingsRequest(Actor, true, "smtp.example.org", 587, "not-an-email", null, null, false, SmtpTlsMode.None, null);

        var result = await useCase.UpsertMailSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRequest);
    }

    [Fact]
    public async Task Upsert_RejectsClearPasswordCombinedWithNewPassword()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertMailSettingsRequest(Actor, true, "smtp.example.org", 587, "pim@example.org", null, "secret", true, SmtpTlsMode.None, null);

        var result = await useCase.UpsertMailSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRequest);
    }

    [Fact]
    public async Task Test_AcceptsValidRecipientAndDelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new TestMailSettingsRequest(Actor, "recipient@example.org");

        var result = await useCase.TestMailSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(store.TestRequest);
    }

    [Fact]
    public async Task Test_RejectsBlankRecipient()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new TestMailSettingsRequest(Actor, "  ");

        var result = await useCase.TestMailSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(store.TestRequest);
    }

    [Fact]
    public async Task Purge_AcceptsValidRequestAndDelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new PurgeMailNotificationOutboxRequest(Actor);

        var result = await useCase.PurgeMailNotificationOutboxAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.PurgeRequest);
    }

    [Fact]
    public async Task Purge_RejectsActorOutsideConfiguredDirectoryScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var foreignActor = new AdministrationActor(Guid.NewGuid(), Guid.NewGuid());
        var request = new PurgeMailNotificationOutboxRequest(foreignActor);

        var result = await useCase.PurgeMailNotificationOutboxAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.PurgeRequest);
    }

    private static AdministrationUseCases CreateUseCase(FakeStore store) => new(store, null!, null!, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"));
    private static AdministrationAuditContext AuditContext() => new(Guid.NewGuid(), "test", null);

    private sealed class FakeStore : IAdministrationDataStore
    {
        public UpsertMailSettingsRequest? UpsertRequest { get; private set; }
        public TestMailSettingsRequest? TestRequest { get; private set; }

        public Task<IReadOnlyList<ManagedPerson>> ListPersonsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedPerson>>([]);
        public Task<AdministrationUpdateResult> DeactivatePersonAsync(DeactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> ReactivatePersonAsync(ReactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> CreatePersonFromDirectoryAccountAsync(CreatePersonFromDirectoryAccountRequest request, ResolvedDirectoryAccount account, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<ManagedPersonDetail?> GetPersonDetailAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult<ManagedPersonDetail?>(null);
        public Task<AdministrationUpdateResult> CreatePersonAccountLinkAsync(CreatePersonAccountLinkRequest request, ResolvedDirectoryAccount account, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> UpdatePersonAccountLinkPurposesAsync(UpdatePersonAccountLinkPurposesRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
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
        public Task<AdministrationUpdateResult> RotateTotpProtectionCertificateAsync(RotateTotpProtectionCertificateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken) => Task.FromResult(new CertificateOverview([]));
        public Task<MailSettingsSnapshot> GetMailSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new MailSettingsSnapshot(false, true, string.Empty, 0, string.Empty, null, false, SmtpTlsMode.None, string.Empty));
        public Task<MailNotificationOutboxStatus> GetMailNotificationOutboxStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new MailNotificationOutboxStatus(0, 0));
        public Task<AdministrationUpdateResult> PurgeMailNotificationOutboxAsync(PurgeMailNotificationOutboxRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { PurgeRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }

        public PurgeMailNotificationOutboxRequest? PurgeRequest { get; private set; }
        public Task<AdministrationUpdateResult> UpsertMailSettingsAsync(UpsertMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { UpsertRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<MailSettingsTestResult> TestMailSettingsAsync(TestMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { TestRequest = request; return Task.FromResult(new MailSettingsTestResult(true, null)); }
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
