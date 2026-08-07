using ADDS.PIM.Application.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>Reads current approver assignment facts from SQL Server; no Active Directory authority.</summary>
public sealed class EfGroupApprovalAuthorizer(PimDbContext dbContext, TimeProvider timeProvider) : IGroupApprovalAuthorizer
{
    public async Task<ApproverIdentity?> ResolveApproverAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await (
            from account in dbContext.DirectoryAccounts.AsNoTracking()
            join link in dbContext.PersonAccountLinks.AsNoTracking() on account.AccountId equals link.AccountId
            join person in dbContext.Persons.AsNoTracking() on link.PersonId equals person.PersonId
            where account.DirectoryScopeId == actor.DirectoryScopeId && account.ObjectGuid == actor.ObjectGuid
                && account.IsActive && account.IsEnabledInDirectory && account.IsWithinAllowedScope
                && person.IsActive && person.ValidFromUtc <= now && (person.ValidUntilUtc == null || now < person.ValidUntilUtc)
                && link.IsActive && link.MayAuthenticate && link.MayApprove
                && link.ValidFromUtc <= now && (link.ValidUntilUtc == null || now < link.ValidUntilUtc)
            select new ApproverIdentity(person.PersonId, account.AccountId, account.DomainQualifiedName))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsApproverForGroupAsync(Guid personId, Guid targetGroupId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await dbContext.GroupApprovers.AsNoTracking().AnyAsync(
            approver => approver.PersonId == personId && approver.TargetGroupId == targetGroupId && approver.IsActive
                && approver.ValidFromUtc <= now && (approver.ValidUntilUtc == null || now < approver.ValidUntilUtc),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListApprovableGroupIdsAsync(Guid personId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await dbContext.GroupApprovers.AsNoTracking()
            .Where(approver => approver.PersonId == personId && approver.IsActive
                && approver.ValidFromUtc <= now && (approver.ValidUntilUtc == null || now < approver.ValidUntilUtc))
            .Select(approver => approver.TargetGroupId)
            .ToListAsync(cancellationToken);
    }
}
