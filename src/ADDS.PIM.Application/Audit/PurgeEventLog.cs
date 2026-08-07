namespace ADDS.PIM.Application.Audit;

public static class PurgeEventLogIds
{
    public const int PurgeIntentRecorded = 4100;
    public const int IdentityPurged = 4101;
    public const int CompletionDeliveryFailed = 4102;
    public const int ReadinessVerified = 4198;
}

public sealed record PurgeEventLogEntry(int EventId, string EventType, Guid CorrelationId, string Payload);

public interface IPurgeEventLog
{
    Task VerifyWritableAsync(CancellationToken cancellationToken);
    Task WriteAsync(PurgeEventLogEntry entry, CancellationToken cancellationToken);
}

public sealed record PurgeEventOutboxMessage(
    Guid OutboxId,
    int EventId,
    string EventType,
    Guid CorrelationId,
    string Payload,
    DateTimeOffset CreatedUtc,
    int DeliveryAttemptCount,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? DeliveredUtc,
    string? LastFailureCategory,
    byte[] RowVersion);

public interface IPurgeEventOutboxStore
{
    Task<IReadOnlyList<PurgeEventOutboxMessage>> ListPendingAsync(int maximumCount, CancellationToken cancellationToken);
    Task<bool> MarkDeliveredAsync(Guid outboxId, byte[] rowVersion, DateTimeOffset deliveredUtc, CancellationToken cancellationToken);
    Task<bool> RecordDeliveryFailureAsync(Guid outboxId, byte[] rowVersion, DateTimeOffset attemptedUtc, string failureCategory, CancellationToken cancellationToken);
}
