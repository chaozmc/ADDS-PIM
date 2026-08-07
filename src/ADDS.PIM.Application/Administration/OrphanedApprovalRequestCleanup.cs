using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Administration;

public sealed record OrphanedApprovalRequestCandidate(
    Guid RequestId,
    string PersonDisplayName,
    string TargetAccountDisplayName,
    string TargetGroupDisplayName,
    DateTimeOffset CreatedUtc,
    DateTimeOffset EnteredAwaitingApprovalUtc);

public enum OrphanedApprovalRequestCleanupResult { Expired, NotFound, NoLongerEligible }

/// <summary>
/// A membership request that reaches AwaitingApproval and is never decided by an approver stays pending
/// indefinitely - approval deliberately has no automatic timeout. This is the admin-triggered, deliberately
/// non-background cleanup for stale approvals, mirroring OrphanedSecondFactorRequestCleanupUseCase so the
/// resulting audit trail stays attributed to the administrator who triggered it. Nothing is deleted; the
/// request, its status history and all audit events are retained.
/// </summary>
public interface IOrphanedApprovalRequestStore
{
    Task<IReadOnlyList<OrphanedApprovalRequestCandidate>> ListCandidatesAsync(CancellationToken cancellationToken);

    Task<OrphanedApprovalRequestCleanupResult> ExpireAsync(Guid requestId, DateTimeOffset now, AdministrationActor administrator, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken);
}

public sealed class OrphanedApprovalRequestCleanupUseCase(
    IOrphanedApprovalRequestStore store,
    DirectoryScopeConfiguration directoryScope,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<OrphanedApprovalRequestCandidate>> ListCandidatesAsync(AdministrationActor actor, CancellationToken cancellationToken)
        => actor.DirectoryScopeId != directoryScope.DirectoryScopeId || actor.ObjectGuid == Guid.Empty
            ? Task.FromResult<IReadOnlyList<OrphanedApprovalRequestCandidate>>([])
            : store.ListCandidatesAsync(cancellationToken);

    public Task<OrphanedApprovalRequestCleanupResult> ExpireAsync(AdministrationActor actor, Guid requestId, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
        => actor.DirectoryScopeId != directoryScope.DirectoryScopeId || actor.ObjectGuid == Guid.Empty || requestId == Guid.Empty
            ? Task.FromResult(OrphanedApprovalRequestCleanupResult.NotFound)
            : store.ExpireAsync(requestId, timeProvider.GetUtcNow(), actor, auditContext, cancellationToken);
}
