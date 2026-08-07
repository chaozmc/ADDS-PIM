using System.Security.Cryptography.X509Certificates;
using ADDS.PIM.Application.Security;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfWebSigningCertificateResolver(PimDbContext dbContext, TimeProvider timeProvider) : IWebSigningCertificateResolver
{
    public async Task<X509Certificate2?> ResolveActiveCertificateAsync(string keyId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var entity = await dbContext.WebSigningCertificates.AsNoTracking().SingleOrDefaultAsync(x => x.KeyId == keyId && x.IsActive && x.Purpose == "ApiRequestSigning" && x.ValidFromUtc <= now && (x.ValidUntilUtc == null || now < x.ValidUntilUtc), cancellationToken);
        if (entity is null) return null;
        var certificate = X509CertificateLoader.LoadCertificate(entity.PublicCertificateDer);
        return StringComparer.OrdinalIgnoreCase.Equals(certificate.Thumbprint, entity.Thumbprint)
            && now >= certificate.NotBefore && now <= certificate.NotAfter
            ? certificate
            : null;
    }
}
