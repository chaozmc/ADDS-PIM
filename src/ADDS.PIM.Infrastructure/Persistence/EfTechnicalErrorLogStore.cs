using System.Text;
using System.Text.Json;
using ADDS.PIM.Application.Diagnostics;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>
/// Optional, off-by-default supplementary sink for unhandled-exception
/// diagnostics. A non-empty <see cref="FilePath"/> appends one JSON line per
/// error alongside the database record; it is a convenience for environments
/// where the database itself is briefly unreachable, not a substitute store.
/// </summary>
public sealed record TechnicalErrorLogFileOptions(string? FilePath);

/// <summary>
/// Persists unhandled-exception diagnostics to a dedicated table, kept
/// structurally separate from the append-only, tamper-resistant
/// <c>AuditEvents</c> trail (audit-model.md: internal error details are
/// stored protected, not as audit facts).
/// </summary>
public sealed class EfTechnicalErrorLogStore(PimDbContext dbContext, TechnicalErrorLogFileOptions fileOptions) : ITechnicalErrorLogStore
{
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public async Task RecordAsync(NewTechnicalErrorLogEntry entry, CancellationToken cancellationToken)
    {
        dbContext.TechnicalErrorLogEntries.Add(new TechnicalErrorLogEntryEntity
        {
            ErrorId = entry.ErrorId,
            OccurredUtc = entry.OccurredUtc,
            RequestId = entry.RequestId,
            CorrelationId = entry.CorrelationId,
            HttpMethod = entry.HttpMethod,
            Path = entry.Path,
            StatusCode = entry.StatusCode,
            ExceptionType = entry.ExceptionType,
            Message = entry.Message,
            StackTrace = entry.StackTrace,
            SourceComponent = entry.SourceComponent,
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(fileOptions.FilePath))
        {
            try
            {
                await AppendToFileAsync(entry, cancellationToken);
            }
            catch
            {
                // Best-effort supplementary sink; the database record above is authoritative.
            }
        }
    }

    public async Task<TechnicalErrorLogRecordPage> QueryAsync(TechnicalErrorLogFilter filter, CancellationToken cancellationToken)
    {
        var filtered = dbContext.TechnicalErrorLogEntries.AsNoTracking()
            .Where(entry => !filter.FromUtc.HasValue || entry.OccurredUtc >= filter.FromUtc.Value)
            .Where(entry => !filter.ToUtc.HasValue || entry.OccurredUtc <= filter.ToUtc.Value)
            .Where(entry => !filter.RequestId.HasValue || entry.RequestId == filter.RequestId.Value)
            .Where(entry => !filter.CorrelationId.HasValue || entry.CorrelationId == filter.CorrelationId.Value);

        var totalCount = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderByDescending(entry => entry.OccurredUtc)
            .ThenByDescending(entry => entry.ErrorId)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(entry => new TechnicalErrorLogRecord(
                entry.ErrorId,
                entry.OccurredUtc,
                entry.RequestId,
                entry.CorrelationId,
                entry.HttpMethod,
                entry.Path,
                entry.StatusCode,
                entry.ExceptionType,
                entry.Message,
                entry.StackTrace,
                entry.SourceComponent))
            .ToListAsync(cancellationToken);

        return new(items, totalCount);
    }

    private async Task AppendToFileAsync(NewTechnicalErrorLogEntry entry, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(fileOptions.FilePath!, line, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }
}
