using System.Security.Cryptography.X509Certificates;
using ADDS.PIM.Infrastructure.Worker;

namespace ADDS.PIM.Infrastructure.Mfa;

/// <summary>Shared LocalMachine\My lookup/validation for TOTP secret-protection certificates, used both by the
/// active <see cref="CertificateTotpSecretProtector"/> and by ad hoc rotation/status instances constructed for a
/// specific thumbprint.</summary>
internal static class TotpProtectionCertificateLoader
{
    /// <summary>Finds the certificate by thumbprint without validating its usability - for status/diagnostic
    /// reporting, where an expired or otherwise unusable certificate must still be reported, not hidden.</summary>
    public static X509Certificate2? TryFind(string? thumbprint)
    {
        var normalized = WorkerClientCertificateProvider.NormalizeThumbprint(thumbprint ?? string.Empty);
        if (normalized.Length == 0) return null;
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false).OfType<X509Certificate2>().SingleOrDefault();
    }

    /// <summary>True if the certificate may currently be used to protect (encrypt/decrypt) a TOTP secret: an
    /// accessible RSA private key, KeyEncipherment usage, and within its validity period.</summary>
    public static bool IsUsableForProtection(X509Certificate2 certificate)
    {
        using var key = certificate.GetRSAPrivateKey();
        var allowsKeyEncipherment = certificate.Extensions.OfType<X509KeyUsageExtension>().Any(extension => extension.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment));
        var now = DateTimeOffset.UtcNow;
        return key is not null && allowsKeyEncipherment && now >= certificate.NotBefore && now <= certificate.NotAfter;
    }

    /// <summary>Finds and validates a certificate for actual protect/unprotect use; throws with the same message
    /// <see cref="CertificateTotpSecretProtector"/> has always thrown, for either a missing or an unusable
    /// certificate.</summary>
    public static X509Certificate2 LoadValidated(string? thumbprint)
    {
        var certificate = TryFind(thumbprint) ?? throw new InvalidOperationException("The requested TOTP protection certificate was not found in LocalMachine\\My.");
        if (!IsUsableForProtection(certificate))
        {
            certificate.Dispose();
            throw new InvalidOperationException("The requested TOTP protection certificate is invalid, expired, or its private key is inaccessible.");
        }

        return certificate;
    }
}
