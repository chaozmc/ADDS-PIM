using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Domain.MembershipRequests;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>
/// Candidates are membership requests still AwaitingSecondFactor whose MFA transaction window has already
/// lapsed unconsumed - i.e. genuinely orphaned, not merely pending within their legitimate 5-minute window.
/// </summary>
public sealed class EfOrphanedSecondFactorRequestStore(PimDbContext dbContext, IDirectoryAccountResolver directoryAccountResolver, IMembershipRequestStateStore stateStore) : IOrphanedSecondFactorRequestStore
{
    public async Task<IReadOnlyList<OrphanedSecondFactorRequestCandidate>> ListCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken)
        => await (from request in dbContext.MembershipRequests.AsNoTracking()
                  join transaction in dbContext.MfaTransactions.AsNoTracking() on request.RequestId equals transaction.RequestId
                  where request.Status == MembershipRequestStatus.AwaitingSecondFactor && transaction.ConsumedUtc == null && transaction.ExpiresUtc <= now
                  orderby transaction.ExpiresUtc
                  select new OrphanedSecondFactorRequestCandidate(request.RequestId, request.PersonDisplayNameSnapshot, request.TargetAccountDisplayNameSnapshot, request.TargetGroupDisplayNameSnapshot, request.CreatedUtc, transaction.ExpiresUtc))
            .ToListAsync(cancellationToken);

    public async Task<OrphanedSecondFactorRequestCleanupResult> ExpireAsync(Guid requestId, DateTimeOffset now, AdministrationActor administrator, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        var request = await dbContext.MembershipRequests.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
        if (request is null) return OrphanedSecondFactorRequestCleanupResult.NotFound;

        var transaction = await dbContext.MfaTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
        if (request.Status != MembershipRequestStatus.AwaitingSecondFactor || transaction is null || transaction.ConsumedUtc is not null || transaction.ExpiresUtc > now)
            return OrphanedSecondFactorRequestCleanupResult.NoLongerEligible;

        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(administrator, cancellationToken);
        try
        {
            await stateStore.TransitionAsync(requestId, MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.Expired,
                auditContext with { AdministratorAccountId = administratorAccountId, AdministratorDisplayName = administratorDisplayName },
                "Administrator-triggered cleanup: MFA transaction window elapsed without completion.", cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Concurrently changed (e.g. a legitimate verification just completed) between the eligibility
            // check above and the transition itself - TransitionAsync's optimistic precondition caught it.
            return OrphanedSecondFactorRequestCleanupResult.NoLongerEligible;
        }

        return OrphanedSecondFactorRequestCleanupResult.Expired;
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
