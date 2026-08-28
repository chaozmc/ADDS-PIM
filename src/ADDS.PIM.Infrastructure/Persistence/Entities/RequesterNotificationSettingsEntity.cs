namespace ADDS.PIM.Infrastructure.Persistence.Entities;

/// <summary>Single-row policy for the requester outcome notification (the email sent to the person who submitted
/// a membership request, distinct from <c>GroupNotificationRecipients</c>): a global Cc/Bcc applied to every such
/// email, e.g. so a security team gets a copy of every user-facing outcome mail. Deliberately separate from
/// <see cref="MailSettingsEntity"/>, which is SMTP transport configuration, not recipient policy.</summary>
public sealed class RequesterNotificationSettingsEntity
{
    public Guid RequesterNotificationSettingsId { get; set; }
    public string? CcAddress { get; set; }
    public string? BccAddress { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public required string UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
