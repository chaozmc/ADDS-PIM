namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class PurgeEventOutboxEntity
{
    public Guid OutboxId { get; set; }
    public int EventId { get; set; }
    public required string EventType { get; set; }
    public Guid CorrelationId { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public int DeliveryAttemptCount { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? DeliveredUtc { get; set; }
    public string? LastFailureCategory { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
