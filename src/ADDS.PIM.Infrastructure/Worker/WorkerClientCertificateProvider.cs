using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ADDS.PIM.Infrastructure.Worker;

internal sealed class WorkerClientCertificateProvider
{
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    public X509Certificate2 LoadClientCertificate(string thumbprint)
    {
        var normalized = NormalizeThumbprint(thumbprint);
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("WorkerClient:ClientCertificateThumbprint is required.");
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var certificate = store.Certificates
            .Find(X509FindType.FindByThumbprint, normalized, validOnly: false)
            .OfType<X509Certificate2>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException("The configured API-to-Worker client certificate was not found in LocalMachine\\My.");

        if (!certificate.HasPrivateKey || !HasEku(certificate, ClientAuthenticationOid))
        {
            throw new InvalidOperationException("The configured API-to-Worker client certificate is not valid for client authentication.");
        }

        var now = DateTimeOffset.UtcNow;
        if (now < certificate.NotBefore || now > certificate.NotAfter)
        {
            throw new InvalidOperationException("The configured API-to-Worker client certificate is outside its validity period.");
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        if (!chain.Build(certificate))
        {
            throw new CryptographicException("The configured API-to-Worker client certificate chain is not trusted.");
        }

        return certificate;
    }

    public static string NormalizeThumbprint(string? thumbprint)
        => string.Concat((thumbprint ?? string.Empty).Where(char.IsAsciiHexDigit)).ToUpperInvariant();

    private static bool HasEku(X509Certificate2 certificate, string requiredEkuOid)
        => certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(oid => StringComparer.Ordinal.Equals(oid.Value, requiredEkuOid));
}
