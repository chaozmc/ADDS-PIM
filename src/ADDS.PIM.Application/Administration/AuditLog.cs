using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Administration;

public sealed record AuditLogFilter(
    int PageNumber,
    int PageSize,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? EventType,
    string? Result,
    Guid? CorrelationId,
    string? ActorAccount);

public sealed record AuditLogRecord(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredUtc,
    string? PersonDisplayNameSnapshot,
    string? ActorAccountDisplayNameSnapshot,
    string? TargetAccountDisplayNameSnapshot,
    string? TargetGroupDisplayNameSnapshot,
    string SourceComponent,
    string? SourceIpAddress,
    string? ClientSourceIpAddress,
    Guid CorrelationId,
    Guid? RequestId,
    long? RequestedTtlSeconds,
    string Result,
    string? FailureCategory,
    string AuthenticationMethod,
    string FrontendClientId);

public sealed record AuditLogRecordPage(
    IReadOnlyList<AuditLogRecord> Items,
    int TotalCount,
    IReadOnlyList<string> AvailableEventTypes,
    IReadOnlyList<string> AvailableResults,
    IReadOnlyList<string> AvailableActorAccounts);

/// <summary>
/// Read-only projection of append-only <c>AuditEvents</c> rows. This is a
/// display surface only; it must never be used as authorization evidence and
/// must not mutate audit facts.
/// </summary>
public interface IAuditLogStore
{
    Task<AuditLogRecordPage> QueryAsync(AuditLogFilter filter, CancellationToken cancellationToken);
}

public sealed class AuditLogUseCase(IAuditLogStore store, DirectoryScopeConfiguration directoryScope)
{
    public async Task<AuditLogRecordPage?> QueryAsync(QueryAuditLogRequest request, CancellationToken cancellationToken)
    {
        if (request.Actor.DirectoryScopeId != directoryScope.DirectoryScopeId
            || request.Actor.ObjectGuid == Guid.Empty
            || request.PageNumber < 1
            || request.PageSize is not (10 or 20 or 30 or 40 or 50)
            || (request.FromUtc.HasValue && request.ToUtc.HasValue && request.FromUtc > request.ToUtc))
        {
            return null;
        }

        return await store.QueryAsync(
            new(request.PageNumber, request.PageSize, request.FromUtc, request.ToUtc, request.EventType, request.Result, request.CorrelationId, request.ActorAccount),
            cancellationToken);
    }
}
