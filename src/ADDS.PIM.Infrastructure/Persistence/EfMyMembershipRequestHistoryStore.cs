using ADDS.PIM.Application.MembershipRequests;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>
/// Projects immutable request snapshots without loading entities or exposing
/// requests belonging to another person.
/// </summary>
public sealed class EfMyMembershipRequestHistoryStore(PimDbContext dbContext) : IMyMembershipRequestHistoryStore
{
    public async Task<MembershipRequestHistoryPage> ListForPersonAsync(
        Guid personId,
        MembershipRequestHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        var allRequests = dbContext.MembershipRequests
            .AsNoTracking()
            .Where(request => request.PersonId == personId);
        var filteredRequests = allRequests
            .Where(request => !filter.Year.HasValue || request.CreatedUtc.Year == filter.Year.Value)
            .Where(request => !filter.Month.HasValue || request.CreatedUtc.Month == filter.Month.Value)
            .Where(request => !filter.EntitlementId.HasValue || request.EntitlementId == filter.EntitlementId.Value)
            .Where(request => !filter.TargetGroupId.HasValue || request.TargetGroupId == filter.TargetGroupId.Value);

        var totalCount = await filteredRequests.CountAsync(cancellationToken);
        var items = await filteredRequests
            .OrderByDescending(request => request.CreatedUtc)
            .ThenByDescending(request => request.RequestId)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(request => new MembershipRequestHistoryItem(
                request.RequestId,
                request.EntitlementId,
                request.TargetGroupId,
                request.TargetAccountDisplayNameSnapshot,
                request.TargetGroupDisplayNameSnapshot,
                request.RequestedTtlSeconds,
                request.CreatedUtc,
                request.Status,
                request.Reason,
                request.TicketReference))
            .ToListAsync(cancellationToken);

        var years = await allRequests.Select(request => request.CreatedUtc.Year).Distinct().OrderByDescending(year => year).ToListAsync(cancellationToken);
        var entitlements = await allRequests
            .Select(request => new { request.EntitlementId, request.TargetGroupDisplayNameSnapshot })
            .Distinct()
            .OrderBy(option => option.TargetGroupDisplayNameSnapshot).ThenBy(option => option.EntitlementId)
            .Select(option => new MembershipRequestHistoryFilterOption(option.EntitlementId, option.TargetGroupDisplayNameSnapshot))
            .ToListAsync(cancellationToken);
        var targetGroups = await allRequests
            .Select(request => new { request.TargetGroupId, request.TargetGroupDisplayNameSnapshot })
            .Distinct()
            .OrderBy(option => option.TargetGroupDisplayNameSnapshot).ThenBy(option => option.TargetGroupId)
            .Select(option => new MembershipRequestHistoryFilterOption(option.TargetGroupId, option.TargetGroupDisplayNameSnapshot))
            .ToListAsync(cancellationToken);

        return new(items, totalCount, years, entitlements, targetGroups);
    }
}
