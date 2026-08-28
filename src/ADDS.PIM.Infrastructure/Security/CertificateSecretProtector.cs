using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ADDS.PIM.Application.Security;
using ADDS.PIM.Infrastructure.Mfa;
using Microsoft.Extensions.Options;

namespace ADDS.PIM.Infrastructure.Security;

/// <summary>
/// Certificate-backed secret protector. The private key must be ACLed to
/// the API identity; no key material is persisted in SQL or configuration.
/// </summary>
public sealed class CertificateSecretProtector : ICertificateSecretProtector, IDisposable
{
    private readonly X509Certificate2 certificate;

    /// <summary>DI-registered singleton, backed by the configured <c>TotpSecretProtection:CertificateThumbprint</c>. Used for every normal enrollment/verification/mail-settings flow.</summary>
    public CertificateSecretProtector(IOptions<TotpSecretProtectionOptions> options)
        : this(options.Value.CertificateThumbprint ?? throw new InvalidOperationException("TotpSecretProtection:CertificateThumbprint is required."))
    {
    }

    /// <summary>Constructs a protector for an arbitrary certificate thumbprint - used by <see cref="CertificateSecretProtectorFactory"/> to build the outgoing/incoming protectors for a certificate rotation, independent of the singleton's configured thumbprint.</summary>
    public CertificateSecretProtector(string thumbprint)
    {
        certificate = TotpProtectionCertificateLoader.LoadValidated(thumbprint);
        KeyId = certificate.Thumbprint;
    }

    public string KeyId { get; }

    public byte[] Protect(ReadOnlySpan<byte> secret)
    {
        using var key = certificate.GetRSAPublicKey() ?? throw new InvalidOperationException("The protection certificate has no RSA public key.");
        return key.Encrypt(secret, RSAEncryptionPadding.OaepSHA256);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, string keyId)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(KeyId, keyId))
        {
            throw new CryptographicException("The persisted secret is protected by a different certificate key ID.");
        }

        using var key = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("The protection certificate private key is unavailable.");
        return key.Decrypt(protectedSecret, RSAEncryptionPadding.OaepSHA256);
    }

    public void Dispose() => certificate.Dispose();
}
