using System.Diagnostics;
using Microsoft.Win32;
using ADDS.PIM.Application.Audit;

namespace ADDS.PIM.Infrastructure.Audit;

public sealed class WindowsPurgeEventLog : IPurgeEventLog
{
    public const string LogName = "ADDS-PIM";
    public const string SourceName = "ADDS.PIM.Api";

    public Task VerifyWritableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The API audit Event Log requires Windows.");
        using var sourceKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\EventLog\{LogName}\{SourceName}");
        if (sourceKey is null) throw new InvalidOperationException($"Windows Event Log source '{SourceName}' is not registered in '{LogName}'.");

        EventLog.WriteEntry(SourceName, "PIM API Event Log readiness verification succeeded.", EventLogEntryType.Information, PurgeEventLogIds.ReadinessVerified);
        return Task.CompletedTask;
    }

    public Task WriteAsync(PurgeEventLogEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The API audit Event Log requires Windows.");
        if (entry.EventId is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(entry));
        EventLog.WriteEntry(SourceName, $"eventType={entry.EventType};correlationId={entry.CorrelationId:D};{entry.Payload}", EventLogEntryType.Information, entry.EventId);
        return Task.CompletedTask;
    }
}
