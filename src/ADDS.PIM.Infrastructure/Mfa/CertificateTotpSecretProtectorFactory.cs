using ADDS.PIM.Application.Mfa;

namespace ADDS.PIM.Infrastructure.Mfa;

public sealed class CertificateTotpSecretProtectorFactory : ITotpSecretProtectorFactory
{
    public ITotpSecretProtector CreateForThumbprint(string thumbprint) => new CertificateTotpSecretProtector(thumbprint);
}
