using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Tests.Administration;

/// <summary>Validation performed by <see cref="AdministrationUseCases.RotateTotpProtectionCertificateAsync"/> before the store is ever invoked - the typed confirmation gate and actor/thumbprint checks.</summary>
public sealed class TotpProtectionCertificateRotationUseCaseTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly AdministrationActor Actor = new(ScopeId, Guid.NewGuid());
    private const string IncomingThumbprint = "1111111111111111111111111111111111AAAA";

    [Fact]
    public async Task Rotate_AcceptsExactConfirmationAndDelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new RotateTotpProtectionCertificateRequest(Actor, IncomingThumbprint, $"ROTATE {IncomingThumbprint}");

        var result = await useCase.RotateTotpProtectionCertificateAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.Same(request, store.RotateRequest);
    }

    [Fact]
    public async Task Rotate_RejectsMismatchedConfirmation()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new RotateTotpProtectionCertificateRequest(Actor, IncomingThumbprint, "ROTATE wrong-thumbprint");

        var result = await useCase.RotateTotpProtectionCertificateAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.RotateRequest);
    }

    [Fact]
    public async Task Rotate_RejectsBlankIncomingThumbprint()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var request = new RotateTotpProtectionCertificateRequest(Actor, "  ", "ROTATE   ");

        var result = await useCase.RotateTotpProtectionCertificateAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.RotateRequest);
    }

    [Fact]
    public async Task Rotate_RejectsActorOutsideConfiguredDirectoryScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var foreignActor = new AdministrationActor(Guid.NewGuid(), Guid.NewGuid());
        var request = new RotateTotpProtectionCertificateRequest(foreignActor, IncomingThumbprint, $"ROTATE {IncomingThumbprint}");

        var result = await useCase.RotateTotpProtectionCertificateAsync(request, AuditContext(), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.RotateRequest);
    }

    [Fact]
    public async Task GetStatus_DelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);

        var status = await useCase.GetTotpProtectionCertificateStatusAsync(CancellationToken.None);

        Assert.True(status.Found);
    }

    [Fact]
    public async Task GetCertificateOverview_DelegatesToStore()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);

        var overview = await useCase.GetCertificateOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview.Certificates);
    }

    private static AdministrationUseCases CreateUseCase(FakeStore store) => new(store, null!, null!, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"));
    private static AdministrationAuditContext AuditContext() => new(Guid.NewGuid(), "test", null);

    private sealed class FakeStore : IAdministrationDataStore
    {
        public RotateTotpProtectionCertificateRequest? RotateRequest { get; private set; }
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
        public Task<TotpProtectionCertificateStatus> GetTotpProtectionCertificateStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new TotpProtectionCertificateStatus(true, "THUMBPRINT", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365), true, 0, 0));
        public Task<AdministrationUpdateResult> RotateTotpProtectionCertificateAsync(RotateTotpProtectionCertificateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { RotateRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken) => Task.FromResult(new CertificateOverview([]));
    }
}
