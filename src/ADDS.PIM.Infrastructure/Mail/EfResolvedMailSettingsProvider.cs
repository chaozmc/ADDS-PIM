using System.Security.Cryptography;
using System.Text;
using ADDS.PIM.Application.Security;
using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Mail;

public sealed class EfResolvedMailSettingsProvider(PimDbContext dbContext, ICertificateSecretProtector certificateSecretProtector) : IResolvedMailSettingsProvider
{
    public async Task<ResolvedMailSettingsResult> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.MailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is null) return new ResolvedMailSettingsResult(ResolvedMailSettingsStatus.NotConfigured, null);
        if (!settings.IsEnabled) return new ResolvedMailSettingsResult(ResolvedMailSettingsStatus.Disabled, null);

        string? password = null;
        if (settings.EncryptedPassword is not null && settings.ProtectionKeyId is not null)
        {
            if (!string.Equals(settings.ProtectionKeyId, certificateSecretProtector.KeyId, StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedMailSettingsResult(ResolvedMailSettingsStatus.ProtectionKeyMismatch, null);
            }
            var plaintext = certificateSecretProtector.Unprotect(settings.EncryptedPassword, settings.ProtectionKeyId);
            try { password = Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }

        var resolved = new ResolvedMailSettings(settings.SmtpHost, settings.SmtpPort, settings.SenderAddress, (SmtpTlsMode)settings.TlsMode, settings.Username, password);
        return new ResolvedMailSettingsResult(ResolvedMailSettingsStatus.Resolved, resolved);
    }
}
