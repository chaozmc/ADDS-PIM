using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Domain.MembershipRequests;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>Candidates are membership requests still AwaitingApproval - approval has no automatic timeout, so any such request is a cleanup candidate for an administrator to review.</summary>
public sealed class EfOrphanedApprovalRequestStore(PimDbContext dbContext, IDirectoryAccountResolver directoryAccountResolver, IMembershipRequestStateStore stateStore) : IOrphanedApprovalRequestStore
{
    public async Task<IReadOnlyList<OrphanedApprovalRequestCandidate>> ListCandidatesAsync(CancellationToken cancellationToken)
        => await (from request in dbContext.MembershipRequests.AsNoTracking()
                  join history in dbContext.MembershipRequestStatusHistory.AsNoTracking() on request.RequestId equals history.RequestId
                  where request.Status == MembershipRequestStatus.AwaitingApproval && history.NewStatus == MembershipRequestStatus.AwaitingApproval
                  orderby history.OccurredUtc
                  select new OrphanedApprovalRequestCandidate(request.RequestId, request.PersonDisplayNameSnapshot, request.TargetAccountDisplayNameSnapshot, request.TargetGroupDisplayNameSnapshot, request.CreatedUtc, history.OccurredUtc))
            .ToListAsync(cancellationToken);

    public async Task<OrphanedApprovalRequestCleanupResult> ExpireAsync(Guid requestId, DateTimeOffset now, AdministrationActor administrator, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        var request = await dbContext.MembershipRequests.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
        if (request is null) return OrphanedApprovalRequestCleanupResult.NotFound;
        if (request.Status != MembershipRequestStatus.AwaitingApproval) return OrphanedApprovalRequestCleanupResult.NoLongerEligible;

        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(administrator, cancellationToken);
        try
        {
            await stateStore.TransitionAsync(requestId, MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Expired,
                auditContext with { AdministratorAccountId = administratorAccountId, AdministratorDisplayName = administratorDisplayName },
                "Administrator-triggered cleanup: no approver decided the request.", cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Concurrently changed (e.g. an approver just decided) between the eligibility check above and the transition itself.
            return OrphanedApprovalRequestCleanupResult.NoLongerEligible;
        }

        return OrphanedApprovalRequestCleanupResult.Expired;
    }

    /// <summary>Mirrors EfAdministrationDataStore.ResolveAdministratorAsync for the same ActorAccountId/display-name audit attribution.</summary>
    private async Task<(Guid? AccountId, string? DisplayName)> ResolveAdministratorAsync(AdministrationActor actor, CancellationToken cancellationToken)
    {
        var local = await dbContext.DirectoryAccounts.AsNoTracking()
            .SingleOrDefaultAsync(account => account.DirectoryScopeId == actor.DirectoryScopeId && account.ObjectGuid == actor.ObjectGuid, cancellationToken);
        if (local is not null) return (local.AccountId, local.DomainQualifiedName);
        var resolved = await directoryAccountResolver.ResolveAccountAsync(actor.ObjectGuid, cancellationToken);
        return (null, resolved?.DomainQualifiedName ?? resolved?.DisplayName);
    }
}
