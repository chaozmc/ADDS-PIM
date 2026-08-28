using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Security;

public sealed record ResolvedMailSettings(string SmtpHost, int SmtpPort, string SenderAddress, SmtpTlsMode TlsMode, string? Username, string? Password);

public enum ResolvedMailSettingsStatus { NotConfigured, ProtectionKeyMismatch, Disabled, Resolved }

public sealed record ResolvedMailSettingsResult(ResolvedMailSettingsStatus Status, ResolvedMailSettings? Settings);

/// <summary>Reads the single persisted <c>MailSettings</c> row and decrypts its SMTP password (if any) with the
/// currently configured <see cref="ICertificateSecretProtector"/>. The one place this decryption happens, so it
/// is never duplicated between the "send test email" flow and the real notification dispatcher.</summary>
public interface IResolvedMailSettingsProvider
{
    Task<ResolvedMailSettingsResult> GetAsync(CancellationToken cancellationToken);
}
