using ADDS.PIM.Application.Security;

namespace ADDS.PIM.Infrastructure.Security;

public sealed class CertificateSecretProtectorFactory : ICertificateSecretProtectorFactory
{
    public ICertificateSecretProtector CreateForThumbprint(string thumbprint) => new CertificateSecretProtector(thumbprint);
}
