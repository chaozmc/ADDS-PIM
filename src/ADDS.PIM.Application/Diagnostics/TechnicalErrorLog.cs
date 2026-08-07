using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Diagnostics;

public sealed record NewTechnicalErrorLogEntry(
    Guid ErrorId,
    DateTimeOffset OccurredUtc,
    Guid? RequestId,
    Guid? CorrelationId,
    string HttpMethod,
    string Path,
    int? StatusCode,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string SourceComponent);

public sealed record TechnicalErrorLogFilter(
    int PageNumber,
    int PageSize,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? RequestId,
    Guid? CorrelationId);

public sealed record TechnicalErrorLogRecord(
    Guid ErrorId,
    DateTimeOffset OccurredUtc,
    Guid? RequestId,
    Guid? CorrelationId,
    string HttpMethod,
    string Path,
    int? StatusCode,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string SourceComponent);

public sealed record TechnicalErrorLogRecordPage(IReadOnlyList<TechnicalErrorLogRecord> Items, int TotalCount);

/// <summary>
/// Stores unhandled-exception diagnostics separately from the append-only,
/// tamper-resistant <c>AuditEvents</c> trail (per audit-model.md: internal
/// error details are stored protected, not mixed into the business/security
/// audit log). Never used as authorization or audit evidence.
/// </summary>
public interface ITechnicalErrorLogStore
{
    Task RecordAsync(NewTechnicalErrorLogEntry entry, CancellationToken cancellationToken);

    Task<TechnicalErrorLogRecordPage> QueryAsync(TechnicalErrorLogFilter filter, CancellationToken cancellationToken);
}

public sealed class RecordTechnicalErrorUseCase(ITechnicalErrorLogStore store)
{
    public Task ExecuteAsync(NewTechnicalErrorLogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return store.RecordAsync(entry, cancellationToken);
    }
}

public sealed class TechnicalErrorLogUseCase(ITechnicalErrorLogStore store, DirectoryScopeConfiguration directoryScope)
{
    public async Task<TechnicalErrorLogRecordPage?> QueryAsync(QueryTechnicalErrorLogRequest request, CancellationToken cancellationToken)
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
            new(request.PageNumber, request.PageSize, request.FromUtc, request.ToUtc, request.RequestId, request.CorrelationId),
            cancellationToken);
    }
}
