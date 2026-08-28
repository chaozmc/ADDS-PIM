namespace ADDS.PIM.Application.Notifications;

public sealed record MailNotificationOutboxMessage(
    Guid OutboxId,
    Guid RequestId,
    string ToAddresses,
    string CcAddresses,
    string BccAddresses,
    string Subject,
    string Body,
    DateTimeOffset CreatedUtc,
    int DeliveryAttemptCount,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? DeliveredUtc,
    string? LastFailureMessage,
    byte[] RowVersion);

public interface IMailNotificationOutboxStore
{
    Task<IReadOnlyList<MailNotificationOutboxMessage>> ListPendingAsync(int maximumCount, CancellationToken cancellationToken);
    Task<bool> MarkDeliveredAsync(Guid outboxId, byte[] rowVersion, DateTimeOffset deliveredUtc, CancellationToken cancellationToken);
    Task<bool> RecordDeliveryFailureAsync(Guid outboxId, byte[] rowVersion, DateTimeOffset attemptedUtc, string failureMessage, CancellationToken cancellationToken);
}
