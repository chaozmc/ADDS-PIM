using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ADDS.PIM.AdWorker.Hosting;

public static class WorkerEventLog
{
    public const string LogName = "ADDS-PIM";
    public const string SourceName = "ADDS.PIM.AdWorker";

    public static readonly EventId ServiceStarted = new(4100, "ServiceStarted");
    public static readonly EventId CommandReceived = new(4101, "CommandReceived");
    public static readonly EventId CommandRejected = new(4102, "CommandRejected");
    public static readonly EventId CommandCompleted = new(4103, "CommandCompleted");
    public static readonly EventId CommandCancelled = new(4104, "CommandCancelled");
    public static readonly EventId CommandFailed = new(4105, "CommandFailed");
    public static readonly EventId TlsClientRejected = new(4106, "TlsClientRejected");
    public static readonly EventId UnexpectedFailure = new(4199, "UnexpectedFailure");

    public static void WriteTlsClientRejected(string? thumbprint, string reason)
    {
        try
        {
            EventLog.WriteEntry(
                SourceName,
                $"mTLS client certificate rejected. Thumbprint={thumbprint ?? "<none>"}; Reason={reason}",
                EventLogEntryType.Warning,
                TlsClientRejected.Id);
        }
        catch
        {
            // TLS validation must remain fail-closed even if Event Log is unavailable.
        }
    }
}
