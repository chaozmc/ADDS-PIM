namespace ADDS.PIM.Application.Mfa;

/// <summary>An active (non-revoked) FIDO2 credential, as needed to build allow/exclude lists and verify assertions.</summary>
public sealed record ActiveFido2Credential(
    Guid Fido2CredentialId,
    Guid PersonId,
    byte[] CredentialId,
    byte[] PublicKey,
    long SignatureCounter,
    string? Label,
    DateTimeOffset CreatedUtc);

public sealed record NewStoredFido2Credential(
    Guid Fido2CredentialId,
    Guid PersonId,
    byte[] CredentialId,
    byte[] PublicKey,
    long SignatureCounter,
    string? Aaguid,
    string? Label,
    DateTimeOffset CreatedUtc);

/// <summary>
/// Person-scoped storage for enrolled FIDO2 credentials. Unlike TOTP (at most one active factor), a
/// person may hold several active credentials at once.
/// </summary>
public interface IFido2CredentialStore
{
    Task<int> CountActiveAsync(Guid personId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActiveFido2Credential>> ListActiveAsync(Guid personId, CancellationToken cancellationToken);

    Task CreateAsync(NewStoredFido2Credential credential, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken);

    /// <summary>Advances the anti-clone signature counter after a successful assertion.</summary>
    Task UpdateSignatureCounterAsync(Guid fido2CredentialId, long signatureCounter, CancellationToken cancellationToken);

    /// <summary>Records a successful FIDO2 step-up (proving an already-active passkey before a further credential may be enrolled) and advances its anti-clone signature counter.</summary>
    Task RecordStepUpSuccessAsync(Guid fido2CredentialId, Guid personId, long newSignatureCounter, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken);

    /// <summary>Audits a rejected FIDO2 step-up attempt (ceremony/assertion rejected).</summary>
    Task RecordStepUpFailureAsync(Guid personId, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken);
}
