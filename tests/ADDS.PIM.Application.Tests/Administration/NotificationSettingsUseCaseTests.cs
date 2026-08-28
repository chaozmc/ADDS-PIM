using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Tests.Administration;

/// <summary>Validation performed by <see cref="AdministrationUseCases.UpsertGroupNotificationRecipientAsync"/>,
/// <see cref="AdministrationUseCases.DeleteGroupNotificationRecipientAsync"/> and
/// <see cref="AdministrationUseCases.UpsertNotificationTemplateAsync"/> before the store is ever invoked.</summary>
public sealed class NotificationSettingsUseCaseTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly AdministrationActor Actor = new(ScopeId, Guid.NewGuid());
    private static readonly Guid TargetGroupId = Guid.NewGuid();

    [Fact]
    public async Task UpsertRecipient_AcceptsValidRequestAndDelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertGroupNotificationRecipientRequest(Actor, Guid.Empty, TargetGroupId, "ops@example.org", MailRecipientType.To, true, null);

        var result = await useCase.UpsertGroupNotificationRecipientAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.UpsertRecipientRequest);
    }

    [Fact]
    public async Task UpsertRecipient_RejectsInvalidEmailAddress()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertGroupNotificationRecipientRequest(Actor, Guid.Empty, TargetGroupId, "not-an-email", MailRecipientType.To, true, null);

        var result = await useCase.UpsertGroupNotificationRecipientAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRecipientRequest);
    }

    [Fact]
    public async Task UpsertRecipient_RejectsUndefinedRecipientType()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertGroupNotificationRecipientRequest(Actor, Guid.Empty, TargetGroupId, "ops@example.org", (MailRecipientType)99, true, null);

        var result = await useCase.UpsertGroupNotificationRecipientAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRecipientRequest);
    }

    [Fact]
    public async Task UpsertRecipient_RejectsActorOutsideConfiguredDirectoryScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var foreignActor = new AdministrationActor(Guid.NewGuid(), Guid.NewGuid());
        var request = new UpsertGroupNotificationRecipientRequest(foreignActor, Guid.Empty, TargetGroupId, "ops@example.org", MailRecipientType.To, true, null);

        var result = await useCase.UpsertGroupNotificationRecipientAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRecipientRequest);
    }

    [Fact]
    public async Task DeleteRecipient_RejectsMissingRowVersion()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new DeleteGroupNotificationRecipientRequest(Actor, Guid.NewGuid(), string.Empty);

        var result = await useCase.DeleteGroupNotificationRecipientAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.DeleteRecipientRequest);
    }

    [Fact]
    public async Task UpsertTemplate_AcceptsValidRequestAndDelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertNotificationTemplateRequest(Actor, "membership-request-outcome", "Subject", "Body", null);

        var result = await useCase.UpsertNotificationTemplateAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.UpsertTemplateRequest);
    }

    [Fact]
    public async Task UpsertTemplate_RejectsBlankSubject()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertNotificationTemplateRequest(Actor, "membership-request-outcome", "  ", "Body", null);

        var result = await useCase.UpsertNotificationTemplateAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertTemplateRequest);
    }

    [Fact]
    public async Task UpsertTemplate_RejectsBlankBody()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertNotificationTemplateRequest(Actor, "membership-request-outcome", "Subject", "  ", null);

        var result = await useCase.UpsertNotificationTemplateAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertTemplateRequest);
    }

    [Fact]
    public async Task UpsertRequesterNotificationSettings_AcceptsBothAddressesNull()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertRequesterNotificationSettingsRequest(Actor, null, null, null);

        var result = await useCase.UpsertRequesterNotificationSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.UpsertRequesterNotificationSettingsRequest);
    }

    [Fact]
    public async Task UpsertRequesterNotificationSettings_AcceptsValidCcAndBcc()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertRequesterNotificationSettingsRequest(Actor, "security@example.org", "audit@example.org", null);

        var result = await useCase.UpsertRequesterNotificationSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.UpsertRequesterNotificationSettingsRequest);
    }

    [Fact]
    public async Task UpsertRequesterNotificationSettings_RejectsInvalidCcAddress()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertRequesterNotificationSettingsRequest(Actor, "not-an-email", null, null);

        var result = await useCase.UpsertRequesterNotificationSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRequesterNotificationSettingsRequest);
    }

    [Fact]
    public async Task UpsertRequesterNotificationSettings_RejectsInvalidBccAddress()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpsertRequesterNotificationSettingsRequest(Actor, null, "not-an-email", null);

        var result = await useCase.UpsertRequesterNotificationSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRequesterNotificationSettingsRequest);
    }

    [Fact]
    public async Task UpsertRequesterNotificationSettings_RejectsActorOutsideConfiguredDirectoryScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var foreignActor = new AdministrationActor(Guid.NewGuid(), Guid.NewGuid());
        var request = new UpsertRequesterNotificationSettingsRequest(foreignActor, null, null, null);

        var result = await useCase.UpsertRequesterNotificationSettingsAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpsertRequesterNotificationSettingsRequest);
    }

    [Fact]
    public async Task SetPersonNotificationEmailOverride_AcceptsValidEmail()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new SetPersonNotificationEmailOverrideRequest(Actor, Guid.NewGuid(), "fallback@example.org", "rowversion");

        var result = await useCase.SetPersonNotificationEmailOverrideAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.SetPersonNotificationEmailOverrideRequest);
    }

    [Fact]
    public async Task SetPersonNotificationEmailOverride_AcceptsNullToClear()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new SetPersonNotificationEmailOverrideRequest(Actor, Guid.NewGuid(), null, "rowversion");

        var result = await useCase.SetPersonNotificationEmailOverrideAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.SetPersonNotificationEmailOverrideRequest);
    }

    [Fact]
    public async Task SetPersonNotificationEmailOverride_RejectsInvalidEmailFormat()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new SetPersonNotificationEmailOverrideRequest(Actor, Guid.NewGuid(), "not-an-email", "rowversion");

        var result = await useCase.SetPersonNotificationEmailOverrideAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.SetPersonNotificationEmailOverrideRequest);
    }

    [Fact]
    public async Task SetPersonNotificationEmailOverride_RejectsMissingRowVersion()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new SetPersonNotificationEmailOverrideRequest(Actor, Guid.NewGuid(), "fallback@example.org", string.Empty);

        var result = await useCase.SetPersonNotificationEmailOverrideAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.SetPersonNotificationEmailOverrideRequest);
    }

    [Fact]
    public async Task UpdateGroupApproverNotificationPreference_AcceptsValidRequestAndDelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpdateGroupApproverNotificationPreferenceRequest(Actor, TargetGroupId, Guid.NewGuid(), false, "rowversion");

        var result = await useCase.UpdateGroupApproverNotificationPreferenceAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.NotNull(store.UpdateGroupApproverNotificationPreferenceRequest);
    }

    [Fact]
    public async Task UpdateGroupApproverNotificationPreference_RejectsMissingRowVersion()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new UpdateGroupApproverNotificationPreferenceRequest(Actor, TargetGroupId, Guid.NewGuid(), true, string.Empty);

        var result = await useCase.UpdateGroupApproverNotificationPreferenceAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpdateGroupApproverNotificationPreferenceRequest);
    }

    [Fact]
    public async Task UpdateGroupApproverNotificationPreference_RejectsActorOutsideConfiguredDirectoryScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var foreignActor = new AdministrationActor(Guid.NewGuid(), Guid.NewGuid());
        var request = new UpdateGroupApproverNotificationPreferenceRequest(foreignActor, TargetGroupId, Guid.NewGuid(), true, "rowversion");

        var result = await useCase.UpdateGroupApproverNotificationPreferenceAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.UpdateGroupApproverNotificationPreferenceRequest);
    }

    private static AdministrationUseCases CreateUseCase(FakeStore store) => new(store, null!, null!, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"));
    private static AdministrationAuditContext AuditContext() => new(Guid.NewGuid(), "test", null);

    private sealed class FakeStore : IAdministrationDataStore
    {
        public UpsertGroupNotificationRecipientRequest? UpsertRecipientRequest { get; private set; }
        public DeleteGroupNotificationRecipientRequest? DeleteRecipientRequest { get; private set; }
        public UpsertNotificationTemplateRequest? UpsertTemplateRequest { get; private set; }

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
        public Task<AdministrationUpdateResult> UpdateGroupApproverNotificationPreferenceAsync(UpdateGroupApproverNotificationPreferenceRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { UpdateGroupApproverNotificationPreferenceRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }

        public UpdateGroupApproverNotificationPreferenceRequest? UpdateGroupApproverNotificationPreferenceRequest { get; private set; }
        public Task<TotpProtectionCertificateStatus> GetTotpProtectionCertificateStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new TotpProtectionCertificateStatus(true, "THUMBPRINT", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365), true, 0, 0));
        public Task<AdministrationUpdateResult> RotateTotpProtectionCertificateAsync(RotateTotpProtectionCertificateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken) => Task.FromResult(new CertificateOverview([]));
        public Task<MailSettingsSnapshot> GetMailSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new MailSettingsSnapshot(false, true, string.Empty, 0, string.Empty, null, false, SmtpTlsMode.None, string.Empty));
        public Task<MailNotificationOutboxStatus> GetMailNotificationOutboxStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new MailNotificationOutboxStatus(0, 0));
        public Task<AdministrationUpdateResult> PurgeMailNotificationOutboxAsync(PurgeMailNotificationOutboxRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<AdministrationUpdateResult> UpsertMailSettingsAsync(UpsertMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(AdministrationUpdateResult.Updated);
        public Task<MailSettingsTestResult> TestMailSettingsAsync(TestMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) => Task.FromResult(new MailSettingsTestResult(true, null));
        public Task<IReadOnlyList<ManagedGroupNotificationRecipient>> ListGroupNotificationRecipientsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedGroupNotificationRecipient>>([]);
        public Task<AdministrationUpdateResult> UpsertGroupNotificationRecipientAsync(UpsertGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { UpsertRecipientRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<AdministrationUpdateResult> DeleteGroupNotificationRecipientAsync(DeleteGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { DeleteRecipientRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<NotificationTemplateSnapshot> GetNotificationTemplateAsync(string templateKey, CancellationToken cancellationToken) => Task.FromResult(new NotificationTemplateSnapshot(templateKey, string.Empty, string.Empty, null));
        public Task<AdministrationUpdateResult> UpsertNotificationTemplateAsync(UpsertNotificationTemplateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { UpsertTemplateRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<RequesterNotificationSettingsSnapshot> GetRequesterNotificationSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new RequesterNotificationSettingsSnapshot(false, null, null, string.Empty));
        public Task<AdministrationUpdateResult> UpsertRequesterNotificationSettingsAsync(UpsertRequesterNotificationSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { UpsertRequesterNotificationSettingsRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<AdministrationUpdateResult> SetPersonNotificationEmailOverrideAsync(SetPersonNotificationEmailOverrideRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { SetPersonNotificationEmailOverrideRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }

        public UpsertRequesterNotificationSettingsRequest? UpsertRequesterNotificationSettingsRequest { get; private set; }
        public SetPersonNotificationEmailOverrideRequest? SetPersonNotificationEmailOverrideRequest { get; private set; }
    }
}
