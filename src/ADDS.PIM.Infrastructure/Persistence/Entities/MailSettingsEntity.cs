namespace ADDS.PIM.Infrastructure.Persistence.Entities;

/// <summary>Single-row SMTP configuration for outbound admin/notification mail. <see cref="EncryptedPassword"/>
/// is protected with the same certificate-backed protector as TOTP secrets (see
/// <see cref="ADDS.PIM.Infrastructure.Security.CertificateSecretProtector"/>); <see cref="ProtectionKeyId"/> is
/// the protecting certificate's thumbprint, re-encrypted on certificate rollover alongside TOTP factors.</summary>
public sealed class MailSettingsEntity
{
    public Guid MailSettingsId { get; set; }
    /// <summary>Global kill switch for the entire mail-notification feature: when <c>false</c>, <see
    /// cref="ADDS.PIM.Infrastructure.Mail.EfResolvedMailSettingsProvider"/> reports
    /// <c>ResolvedMailSettingsStatus.Disabled</c> and the dispatcher defers every pending outbox row exactly like
    /// an unusable SMTP configuration - a soft pause, not a purge: already-queued messages resume sending once
    /// re-enabled. Defaults to <c>true</c> so upgrading an already-configured deployment changes nothing.</summary>
    public bool IsEnabled { get; set; } = true;
    public required string SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public required string SenderAddress { get; set; }
    public string? Username { get; set; }
    public byte[]? EncryptedPassword { get; set; }
    public string? ProtectionKeyId { get; set; }
    public int TlsMode { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public required string UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
