namespace ADDS.PIM.Application.Mfa;

/// <summary>
/// Application-owned ports around the WebAuthn ceremony. Kept free of any FIDO2 library
/// type so Application has no dependency on the concrete library - only Infrastructure references it.
/// Options/response payloads are passed through as opaque JSON strings in the library's own wire format.
/// </summary>
public sealed record Fido2CeremonyOptions(string OptionsJson, byte[] Challenge);

public sealed record NewFido2Credential(byte[] CredentialId, byte[] PublicKey, long SignatureCounter, string? Aaguid);

/// <summary>Builds/validates the "create a new credential" ceremony.</summary>
public interface IFido2RegistrationCeremony
{
    Fido2CeremonyOptions BeginRegistration(Guid personId, string personDisplayName, IReadOnlyList<byte[]> excludeCredentialIds);

    /// <summary>Null on any validation failure (origin, RP ID, challenge mismatch, attestation, user verification).</summary>
    NewFido2Credential? CompleteRegistration(string optionsJson, string attestationResponseJson);
}

public sealed record Fido2AssertionCandidate(byte[] CredentialId, byte[] PublicKey, long SignatureCounter);

public sealed record Fido2AssertionOutcome(byte[] CredentialId, long NewSignatureCounter);

/// <summary>Builds/validates the "prove possession of an existing credential" ceremony.</summary>
public interface IFido2AssertionCeremony
{
    Fido2CeremonyOptions BeginAssertion(IReadOnlyList<byte[]> allowCredentialIds);

    /// <summary>
    /// Null on any validation failure, including a returned signature counter that does not exceed
    /// <see cref="Fido2AssertionCandidate.SignatureCounter"/> for the matched credential (replay/clone
    /// detection).
    /// </summary>
    Fido2AssertionOutcome? CompleteAssertion(string optionsJson, string assertionResponseJson, IReadOnlyList<Fido2AssertionCandidate> candidates);
}
