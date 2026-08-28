using System.Security.Cryptography.X509Certificates;
using ADDS.PIM.Application.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfWebSigningCertificateResolver(PimDbContext dbContext, TimeProvider timeProvider, ILogger<EfWebSigningCertificateResolver> logger) : IWebSigningCertificateResolver
{
    public async Task<X509Certificate2?> ResolveActiveCertificateAsync(string keyId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var entity = await dbContext.WebSigningCertificates.AsNoTracking().SingleOrDefaultAsync(x => x.KeyId == keyId && x.IsActive && x.Purpose == "ApiRequestSigning" && x.ValidFromUtc <= now && (x.ValidUntilUtc == null || now < x.ValidUntilUtc), cancellationToken);
        if (entity is null)
        {
            logger.LogWarning("ResolveActiveCertificateAsync found no active ApiRequestSigning certificate row for KeyId {KeyId} valid at {Now}.", keyId, now);
            return null;
        }
        var certificate = X509CertificateLoader.LoadCertificate(entity.PublicCertificateDer);
        if (!StringComparer.OrdinalIgnoreCase.Equals(certificate.Thumbprint, entity.Thumbprint) || now < certificate.NotBefore || now > certificate.NotAfter)
        {
            logger.LogError("ResolveActiveCertificateAsync data mismatch for KeyId {KeyId}: ThumbprintMismatch={ThumbprintMismatch}, StoredThumbprint={StoredThumbprint}, CertificateThumbprint={CertificateThumbprint}, CertificateNotBefore={CertificateNotBefore}, CertificateNotAfter={CertificateNotAfter}.",
                keyId, !StringComparer.OrdinalIgnoreCase.Equals(certificate.Thumbprint, entity.Thumbprint), entity.Thumbprint, certificate.Thumbprint, certificate.NotBefore, certificate.NotAfter);
            return null;
        }
        return certificate;
    }
}
