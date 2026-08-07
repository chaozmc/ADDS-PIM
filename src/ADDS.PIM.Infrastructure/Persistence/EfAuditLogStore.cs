using ADDS.PIM.Application.Administration;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>
/// Projects append-only audit event rows without mutating them. Filter-option
/// lists are drawn from the same base query so an administrator only sees
/// values that actually occur in the current data.
/// </summary>
public sealed class EfAuditLogStore(PimDbContext dbContext) : IAuditLogStore
{
    public async Task<AuditLogRecordPage> QueryAsync(AuditLogFilter filter, CancellationToken cancellationToken)
    {
        var allEvents = dbContext.AuditEvents.AsNoTracking();
        var filteredEvents = allEvents
            .Where(auditEvent => !filter.FromUtc.HasValue || auditEvent.OccurredUtc >= filter.FromUtc.Value)
            .Where(auditEvent => !filter.ToUtc.HasValue || auditEvent.OccurredUtc <= filter.ToUtc.Value)
            .Where(auditEvent => string.IsNullOrEmpty(filter.EventType) || auditEvent.EventType == filter.EventType)
            .Where(auditEvent => string.IsNullOrEmpty(filter.Result) || auditEvent.Result == filter.Result)
            .Where(auditEvent => !filter.CorrelationId.HasValue || auditEvent.CorrelationId == filter.CorrelationId.Value)
            .Where(auditEvent => string.IsNullOrEmpty(filter.ActorAccount) || auditEvent.ActorAccountDisplayNameSnapshot == filter.ActorAccount);

        var totalCount = await filteredEvents.CountAsync(cancellationToken);
        var items = await filteredEvents
            .OrderByDescending(auditEvent => auditEvent.OccurredUtc)
            .ThenByDescending(auditEvent => auditEvent.EventId)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(auditEvent => new AuditLogRecord(
                auditEvent.EventId,
                auditEvent.EventType,
                auditEvent.OccurredUtc,
                auditEvent.PersonDisplayNameSnapshot,
                auditEvent.ActorAccountDisplayNameSnapshot,
                auditEvent.TargetAccountDisplayNameSnapshot,
                auditEvent.TargetGroupDisplayNameSnapshot,
                auditEvent.SourceComponent,
                auditEvent.SourceIpAddress,
                auditEvent.ClientSourceIpAddress,
                auditEvent.CorrelationId,
                auditEvent.RequestId,
                auditEvent.RequestedTtlSeconds,
                auditEvent.Result,
                auditEvent.FailureCategory,
                auditEvent.AuthenticationMethod,
                auditEvent.FrontendClientId))
            .ToListAsync(cancellationToken);

        var eventTypes = await allEvents.Select(auditEvent => auditEvent.EventType).Distinct().OrderBy(eventType => eventType).ToListAsync(cancellationToken);
        var results = await allEvents.Select(auditEvent => auditEvent.Result).Distinct().OrderBy(result => result).ToListAsync(cancellationToken);
        var actorAccounts = await allEvents
            .Where(auditEvent => auditEvent.ActorAccountDisplayNameSnapshot != null)
            .Select(auditEvent => auditEvent.ActorAccountDisplayNameSnapshot!)
            .Distinct()
            .OrderBy(actorAccount => actorAccount)
            .ToListAsync(cancellationToken);

        return new(items, totalCount, eventTypes, results, actorAccounts);
    }
}
