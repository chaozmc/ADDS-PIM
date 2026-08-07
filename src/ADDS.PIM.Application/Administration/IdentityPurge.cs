using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Audit;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Administration;

public enum PurgeInitiatorType { Person, DirectoryAccount, TargetGroup }

public sealed record PurgeScopePreview(
    PurgeInitiatorType InitiatorType,
    Guid InitiatorId,
    string DisplayName,
    bool IsEligible,
    string? BlockingReason,
    int Persons,
    int DirectoryAccounts,
    int PersonAccountLinks,
    int TargetGroups,
    int GroupPolicies,
    int DirectEntitlements,
    int MembershipRequests,
    int MembershipRequestStatusHistory,
    int MfaTransactions,
    int TotpFactors,
    int TotpUsedTimeSteps,
    int Fido2Credentials,
    string CanonicalScopeSummary);

public sealed record IdentityPurgeCandidate(PurgeInitiatorType InitiatorType, Guid InitiatorId, string DisplayName);

public interface IIdentityPurgeScopeStore
{
    Task<IReadOnlyList<IdentityPurgeCandidate>> ListCandidatesAsync(Guid directoryScopeId, CancellationToken cancellationToken);
    Task<PurgeScopePreview?> GetPreviewAsync(PurgeInitiatorType initiatorType, Guid initiatorId, Guid directoryScopeId, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> ExecuteAsync(PurgeInitiatorType initiatorType, Guid initiatorId, AdministrationActor actor, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
}

public sealed class IdentityPurgeUseCase(IIdentityPurgeScopeStore store, IPurgeEventLog eventLog, DirectoryScopeConfiguration directoryScope)
{
    public Task<IReadOnlyList<IdentityPurgeCandidate>> ListCandidatesAsync(AdministrationActor actor, CancellationToken cancellationToken)
        => actor.DirectoryScopeId != directoryScope.DirectoryScopeId || actor.ObjectGuid == Guid.Empty
            ? Task.FromResult<IReadOnlyList<IdentityPurgeCandidate>>([])
            : store.ListCandidatesAsync(actor.DirectoryScopeId, cancellationToken);

    public Task<PurgeScopePreview?> GetPreviewAsync(AdministrationActor actor, PurgeInitiatorType initiatorType, Guid initiatorId, CancellationToken cancellationToken)
        => actor.DirectoryScopeId != directoryScope.DirectoryScopeId || actor.ObjectGuid == Guid.Empty || initiatorId == Guid.Empty
            ? Task.FromResult<PurgeScopePreview?>(null)
            : store.GetPreviewAsync(initiatorType, initiatorId, actor.DirectoryScopeId, cancellationToken);

    public async Task<AdministrationUpdateResult> ExecuteAsync(AdministrationActor actor, PurgeInitiatorType initiatorType, Guid initiatorId, string confirmation, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (actor.DirectoryScopeId != directoryScope.DirectoryScopeId || actor.ObjectGuid == Guid.Empty || initiatorId == Guid.Empty || !string.Equals(confirmation, $"PURGE {initiatorId:D}", StringComparison.Ordinal)) return AdministrationUpdateResult.Invalid;
        var preview = await store.GetPreviewAsync(initiatorType, initiatorId, actor.DirectoryScopeId, cancellationToken);
        if (preview is null) return AdministrationUpdateResult.NotFound;
        if (!preview.IsEligible) return AdministrationUpdateResult.Conflict;
        await eventLog.WriteAsync(new(PurgeEventLogIds.PurgeIntentRecorded, "PimIdentityPurgeIntentRecorded", auditContext.CorrelationId, $"initiatorType={initiatorType};initiatorId={initiatorId:D};administrator={actor.ObjectGuid:D};{preview.CanonicalScopeSummary}"), cancellationToken);
        return await store.ExecuteAsync(initiatorType, initiatorId, actor, auditContext, cancellationToken);
    }
}
