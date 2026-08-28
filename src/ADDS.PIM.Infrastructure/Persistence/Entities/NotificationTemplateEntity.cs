namespace ADDS.PIM.Infrastructure.Persistence.Entities;

/// <summary>An editable, keyed email template. Only one row exists today
/// (<c>ADDS.PIM.Contracts.Notifications.NotificationTemplateKeys.MembershipRequestOutcome</c>), but the table is
/// keyed by <see cref="TemplateKey"/> rather than a singleton row so further template types can be added later
/// without a schema change.</summary>
public sealed class NotificationTemplateEntity
{
    public Guid NotificationTemplateId { get; set; }
    public required string TemplateKey { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public required string UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
