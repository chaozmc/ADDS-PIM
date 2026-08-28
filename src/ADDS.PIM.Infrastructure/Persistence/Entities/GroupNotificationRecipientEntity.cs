namespace ADDS.PIM.Infrastructure.Persistence.Entities;

/// <summary>One email address that gets notified whenever a membership request for <see cref="TargetGroupId"/>
/// reaches a terminal outcome (see <see cref="MailNotificationOutboxEntity"/>).</summary>
public sealed class GroupNotificationRecipientEntity
{
    public Guid GroupNotificationRecipientId { get; set; }
    public Guid TargetGroupId { get; set; }
    public required string EmailAddress { get; set; }
    /// <summary><see cref="ADDS.PIM.Contracts.Administration.V1.MailRecipientType"/> as int (0=To, 1=Cc, 2=Bcc), mirroring the <c>SmtpTlsMode</c> storage pattern on <c>MailSettingsEntity</c>.</summary>
    public int RecipientType { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
