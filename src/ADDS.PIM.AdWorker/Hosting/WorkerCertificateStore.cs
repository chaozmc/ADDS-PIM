using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ADDS.PIM.AdWorker.Hosting;

public sealed class WorkerCertificateStore(WorkerHostSettings settings)
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    public X509Certificate2 LoadServerCertificate()
    {
        var certificate = FindByThumbprint(settings.ServerCertificateThumbprint)
            ?? throw new InvalidOperationException("The configured Worker server certificate was not found in LocalMachine\\My.");
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The configured Worker server certificate has no accessible private key.");
        }

        ValidateCertificate(certificate, ServerAuthenticationOid, "Worker server");
        return certificate;
    }

    public X509Certificate2 LoadWorkerClientCertificate()
    {
        var certificate = FindByThumbprint(settings.WorkerClientCertificateThumbprint)
            ?? throw new InvalidOperationException("The configured Worker client certificate was not found in LocalMachine\\My.");
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The configured Worker client certificate has no accessible private key.");
        }

        ValidateCertificate(certificate, ClientAuthenticationOid, "Worker client");
        return certificate;
    }

    public bool IsAllowedApiClientCertificate(X509Certificate2? certificate, SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null)
        {
            WorkerEventLog.WriteTlsClientRejected(null, "No client certificate was supplied.");
            return false;
        }

        var thumbprint = WorkerHostSettings.NormalizeThumbprint(certificate.Thumbprint);
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            WorkerEventLog.WriteTlsClientRejected(thumbprint, $"TLS policy errors: {sslPolicyErrors}.");
            return false;
        }

        if (!settings.AllowedApiClientCertificateThumbprints.Contains(thumbprint))
        {
            WorkerEventLog.WriteTlsClientRejected(thumbprint, "Certificate thumbprint is not allowlisted.");
            return false;
        }

        try
        {
            ValidateCertificate(certificate, ClientAuthenticationOid, "API client");
            return true;
        }
        catch (CryptographicException)
        {
            WorkerEventLog.WriteTlsClientRejected(thumbprint, "Certificate chain validation failed.");
            return false;
        }
        catch (InvalidOperationException)
        {
            WorkerEventLog.WriteTlsClientRejected(thumbprint, "Certificate lifecycle or EKU validation failed.");
            return false;
        }
    }

    private static X509Certificate2? FindByThumbprint(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .OfType<X509Certificate2>()
            .SingleOrDefault();
    }

    private static void ValidateCertificate(X509Certificate2 certificate, string requiredEkuOid, string purpose)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < certificate.NotBefore || now > certificate.NotAfter || !HasEku(certificate, requiredEkuOid))
        {
            throw new InvalidOperationException($"The {purpose} certificate is not valid for its intended purpose.");
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (!chain.Build(certificate))
        {
            throw new CryptographicException($"The {purpose} certificate chain is not trusted.");
        }
    }

    private static bool HasEku(X509Certificate2 certificate, string requiredEkuOid)
        => certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(oid => StringComparer.Ordinal.Equals(oid.Value, requiredEkuOid));
}
