namespace ADDS.PIM.Application.Security;

public enum ApiRequestReplayRegistrationKind { Accepted, Existing, Conflict }

public sealed record ApiRequestReplayRegistration(ApiRequestReplayRegistrationKind Kind);

public interface IWebSigningCertificateResolver
{
    Task<System.Security.Cryptography.X509Certificates.X509Certificate2?> ResolveActiveCertificateAsync(string keyId, CancellationToken cancellationToken);
}

public interface IApiRequestReplayStore
{
    Task<ApiRequestReplayRegistration> RegisterAsync(
        ApiRequestSignature signature,
        string canonicalRequestHash,
        DateTimeOffset receivedUtc,
        CancellationToken cancellationToken);
}

/// <summary>Validates a Web certificate signature and persists replay protection before use-case processing.</summary>
public sealed class ValidateApiRequestSignatureUseCase(
    IWebSigningCertificateResolver certificateResolver,
    IApiRequestReplayStore replayStore,
    TimeProvider timeProvider)
{
    public async Task<ApiRequestReplayRegistration> ExecuteAsync(ApiRequestSignature signature, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var certificate = await certificateResolver.ResolveActiveCertificateAsync(signature.KeyId, cancellationToken);
        if (certificate is null || !ApiRequestCanonicalizer.HasValidSignature(signature, certificate))
        {
            return new(ApiRequestReplayRegistrationKind.Conflict);
        }

        var now = timeProvider.GetUtcNow();
        if (signature.IssuedUtc > now || now - signature.IssuedUtc > TimeSpan.FromMinutes(5))
        {
            return new(ApiRequestReplayRegistrationKind.Conflict);
        }

        var canonicalHash = ApiRequestCanonicalizer.ComputeBodyHash(System.Text.Encoding.UTF8.GetBytes(ApiRequestCanonicalizer.CreateCanonicalRepresentation(signature)));
        return await replayStore.RegisterAsync(signature, canonicalHash, now, cancellationToken);
    }
}
