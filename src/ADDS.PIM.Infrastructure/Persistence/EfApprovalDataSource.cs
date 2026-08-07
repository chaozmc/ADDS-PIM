using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Contracts.MembershipRequests.V1;
using ADDS.PIM.Domain.MembershipRequests;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfApprovalDataSource(PimDbContext dbContext) : IApprovalDataSource
{
    public async Task<PendingApprovalsPage> ListPendingApprovalsAsync(IReadOnlyList<Guid> approvableGroupIds, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.MembershipRequests.AsNoTracking()
            .Where(request => request.Status == MembershipRequestStatus.AwaitingApproval && approvableGroupIds.Contains(request.TargetGroupId));

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(request => request.CreatedUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(request => new PendingApprovalItem(
                request.RequestId,
                request.TargetGroupId,
                request.TargetGroupDisplayNameSnapshot,
                request.PersonDisplayNameSnapshot,
                request.TargetAccountDisplayNameSnapshot,
                request.RequestedTtlSeconds,
                request.CreatedUtc,
                request.Reason,
                request.TicketReference))
            .ToListAsync(cancellationToken);

        return new PendingApprovalsPage(items, totalCount);
    }

    public async Task<PendingApprovalRequestDetails?> GetPendingRequestAsync(Guid requestId, CancellationToken cancellationToken)
        => await (from request in dbContext.MembershipRequests.AsNoTracking()
                  join actorAccount in dbContext.DirectoryAccounts.AsNoTracking() on request.ActorAccountId equals actorAccount.AccountId
                  where request.RequestId == requestId
                  select new PendingApprovalRequestDetails(
                      request.RequestId,
                      request.TargetAccountId,
                      request.TargetGroupId,
                      request.RequestedTtlSeconds,
                      request.TicketReference,
                      actorAccount.DirectoryScopeId,
                      actorAccount.ObjectGuid,
                      request.Status))
            .SingleOrDefaultAsync(cancellationToken);
}
