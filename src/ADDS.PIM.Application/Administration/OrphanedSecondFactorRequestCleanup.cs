using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Administration;

public sealed record OrphanedSecondFactorRequestCandidate(
    Guid RequestId,
    string PersonDisplayName,
    string TargetAccountDisplayName,
    string TargetGroupDisplayName,
    DateTimeOffset CreatedUtc,
    DateTimeOffset TransactionExpiresUtc);

public enum OrphanedSecondFactorRequestCleanupResult { Expired, NotFound, NoLongerEligible }

/// <summary>
/// A membership request that reaches AwaitingSecondFactor and is never completed stays pending until its
/// 5-minute MFA transaction window lapses, but nothing marks it Expired on its own (poc-status-and-handoff,
/// "Known MFA gap"). This is the designed, admin-triggered, deliberately non-background cleanup for those
/// orphaned requests, so the resulting audit trail stays attributed to the administrator who triggered it.
/// Nothing is deleted; the request, its status history and all audit events are retained.
/// </summary>
public interface IOrphanedSecondFactorRequestStore
{
    Task<IReadOnlyList<OrphanedSecondFactorRequestCandidate>> ListCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<OrphanedSecondFactorRequestCleanupResult> ExpireAsync(Guid requestId, DateTimeOffset now, AdministrationActor administrator, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken);
}

public sealed class OrphanedSecondFactorRequestCleanupUseCase(
    IOrphanedSecondFactorRequestStore store,
    DirectoryScopeConfiguration directoryScope,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<OrphanedSecondFactorRequestCandidate>> ListCandidatesAsync(AdministrationActor actor, CancellationToken cancellationToken)
        => actor.DirectoryScopeId != directoryScope.DirectoryScopeId || actor.ObjectGuid == Guid.Empty
            ? Task.FromResult<IReadOnlyList<OrphanedSecondFactorRequestCandidate>>([])
            : store.ListCandidatesAsync(timeProvider.GetUtcNow(), cancellationToken);

    public Task<OrphanedSecondFactorRequestCleanupResult> ExpireAsync(AdministrationActor actor, Guid requestId, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
        => actor.DirectoryScopeId != directoryScope.DirectoryScopeId || actor.ObjectGuid == Guid.Empty || requestId == Guid.Empty
            ? Task.FromResult(OrphanedSecondFactorRequestCleanupResult.NotFound)
            : store.ExpireAsync(requestId, timeProvider.GetUtcNow(), actor, auditContext, cancellationToken);
}
