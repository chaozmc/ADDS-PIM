using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ADDS.PIM.Application.Security;

/// <summary>
/// Versioned application-signature metadata for the Web-to-API trust boundary.
/// The Web private key remains in the certificate store; the API receives only
/// an allowlisted public certificate through an infrastructure-owned trust store.
/// </summary>
public sealed record ApiRequestSignature(
    string Version,
    string KeyId,
    Guid RequestId,
    Guid CorrelationId,
    DateTimeOffset IssuedUtc,
    string Nonce,
    string Method,
    string Path,
    string CanonicalQuery,
    string BodyHash,
    string ContentType,
    string ApiVersion,
    string Signature)
{
    public const string CurrentVersion = "api-request-v1";
}

public static class ApiRequestCanonicalizer
{
    public static string CreateCanonicalRepresentation(ApiRequestSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        Validate(signature);
        return string.Join('\n',
        [
            $"version={signature.Version}",
            $"method={signature.Method}",
            $"path={signature.Path}",
            $"query={signature.CanonicalQuery}",
            $"bodyHash={signature.BodyHash}",
            $"requestId={signature.RequestId:D}",
            $"correlationId={signature.CorrelationId:D}",
            $"issuedUtc={signature.IssuedUtc.UtcDateTime:O}",
            $"nonce={signature.Nonce}",
            $"keyId={signature.KeyId}",
            $"contentType={signature.ContentType}",
            $"apiVersion={signature.ApiVersion}"
        ]);
    }

    public static string ComputeBodyHash(ReadOnlySpan<byte> body)
        => Base64UrlEncode(SHA256.HashData(body));

    public static string ComputeSignature(ApiRequestSignature signature, X509Certificate2 signingCertificate)
    {
        ArgumentNullException.ThrowIfNull(signingCertificate);
        using var key = signingCertificate.GetRSAPrivateKey()
            ?? throw new ArgumentException("The signing certificate has no accessible RSA private key.", nameof(signingCertificate));
        return Base64UrlEncode(key.SignData(Encoding.UTF8.GetBytes(CreateCanonicalRepresentation(signature)), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    public static bool HasValidSignature(ApiRequestSignature signature, X509Certificate2 verificationCertificate)
    {
        if (signature is null || string.IsNullOrWhiteSpace(signature.Signature) || verificationCertificate is null) return false;
        try
        {
            if (!verificationCertificate.Extensions.OfType<X509KeyUsageExtension>().Any(extension => extension.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))) return false;
            using var key = verificationCertificate.GetRSAPublicKey();
            var encoded = signature.Signature.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            return key is not null && key.VerifyData(Encoding.UTF8.GetBytes(CreateCanonicalRepresentation(signature with { Signature = string.Empty })), Convert.FromBase64String(encoded), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string CanonicalizeQuery(IEnumerable<KeyValuePair<string, string?>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return string.Join('&', query
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value ?? string.Empty))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => $"{PercentEncode(pair.Key)}={PercentEncode(pair.Value)}"));
    }

    private static void Validate(ApiRequestSignature signature)
    {
        if (!StringComparer.Ordinal.Equals(signature.Version, ApiRequestSignature.CurrentVersion)
            || string.IsNullOrWhiteSpace(signature.KeyId)
            || signature.RequestId == Guid.Empty || signature.CorrelationId == Guid.Empty
            || !IsBase64Url(signature.Nonce) || !IsBase64Url(signature.BodyHash)
            || string.IsNullOrWhiteSpace(signature.Method) || !StringComparer.Ordinal.Equals(signature.Method, signature.Method.ToUpperInvariant())
            || string.IsNullOrWhiteSpace(signature.Path) || !signature.Path.StartsWith("/", StringComparison.Ordinal)
            || signature.Path.Contains('?', StringComparison.Ordinal) || signature.Path.Contains('#', StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(signature.ContentType) || string.IsNullOrWhiteSpace(signature.ApiVersion)
            || signature.CanonicalQuery.Contains('\r') || signature.CanonicalQuery.Contains('\n'))
        {
            throw new ArgumentException("The API request signature metadata is invalid.", nameof(signature));
        }
    }

    private static bool IsBase64Url(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string PercentEncode(string value)
        => string.Concat(Encoding.UTF8.GetBytes(value).Select(byteValue =>
            char.IsAsciiLetterOrDigit((char)byteValue) || byteValue is (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~'
                ? ((char)byteValue).ToString(CultureInfo.InvariantCulture)
                : $"%{byteValue:X2}"));

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
