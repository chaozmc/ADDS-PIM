namespace ADDS.PIM.Infrastructure.Persistence.Entities;

/// <summary>One queued outbound notification email, enqueued by <see cref="EfMembershipRequestStateStore"/> in
/// the same transaction as the membership-request terminal-state transition it reports on. Subject/Body are
/// already rendered at enqueue time (not re-rendered at delivery time), so a later template edit never changes
/// what an already-queued message says, and delivery stays a dumb send-what's-here retry loop
/// (<c>MailNotificationOutboxDispatcher</c>).</summary>
public sealed class MailNotificationOutboxEntity
{
    public Guid OutboxId { get; set; }
    public Guid RequestId { get; set; }
    /// <summary>Semicolon-separated addresses per header; <see cref="CcAddresses"/> and <see cref="BccAddresses"/> are
    /// empty (not null) when the group has no recipients configured for that header.</summary>
    public required string ToAddresses { get; set; }
    public string CcAddresses { get; set; } = string.Empty;
    public string BccAddresses { get; set; } = string.Empty;
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public int DeliveryAttemptCount { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? DeliveredUtc { get; set; }
    public string? LastFailureMessage { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
