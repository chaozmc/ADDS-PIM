using System.Text.Json;
using ADDS.PIM.Application.Mfa;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging;

namespace ADDS.PIM.Infrastructure.Mfa;

/// <summary>
/// The only place in this codebase that touches the <c>Fido2</c> NuGet package directly.
/// Application only ever sees opaque JSON strings and raw byte arrays via
/// <see cref="IFido2RegistrationCeremony"/>/<see cref="IFido2AssertionCeremony"/>.
/// </summary>
public sealed class Fido2WebAuthnCeremony(IFido2 fido2, ILogger<Fido2WebAuthnCeremony> logger) : IFido2RegistrationCeremony, IFido2AssertionCeremony
{
    public Fido2CeremonyOptions BeginRegistration(Guid personId, string personDisplayName, IReadOnlyList<byte[]> excludeCredentialIds)
    {
        var user = new Fido2User
        {
            Id = personId.ToByteArray(),
            Name = personDisplayName,
            DisplayName = personDisplayName
        };

        var excludeCredentials = excludeCredentialIds
            .Select(id => new PublicKeyCredentialDescriptor(id))
            .ToList();

        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = user,
            ExcludeCredentials = excludeCredentials,
            // This app never performs a discoverable/usernameless ceremony - the API always supplies an
            // explicit AllowCredentials list (see BeginAssertion below), so a resident credential buys
            // nothing functionally while consuming an authenticator's limited on-device credential slots
            // (e.g. a hardware security key). This requires user verification for every FIDO2
            // ceremony, including registration - Default leaves it "discouraged", so it is set explicitly.
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Discouraged,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        });

        return new Fido2CeremonyOptions(options.ToJson(), options.Challenge);
    }

    public NewFido2Credential? CompleteRegistration(string optionsJson, string attestationResponseJson)
    {
        var options = CredentialCreateOptions.FromJson(optionsJson);
        var attestationResponse = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationResponseJson)
            ?? throw new ArgumentException("The attestation response is invalid.", nameof(attestationResponseJson));

        try
        {
            var result = fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestationResponse,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true)
            }).GetAwaiter().GetResult();

            return new NewFido2Credential(result.Id, result.PublicKey, result.SignCount, result.AaGuid == Guid.Empty ? null : result.AaGuid.ToString());
        }
        catch (Fido2VerificationException ex)
        {
            logger.LogWarning(ex, "CompleteRegistration failed FIDO2 attestation verification.");
            return null;
        }
    }

    public Fido2CeremonyOptions BeginAssertion(IReadOnlyList<byte[]> allowCredentialIds)
    {
        var allowedCredentials = allowCredentialIds
            .Select(id => new PublicKeyCredentialDescriptor(id))
            .ToList();

        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowedCredentials,
            UserVerification = UserVerificationRequirement.Required
        });

        return new Fido2CeremonyOptions(options.ToJson(), options.Challenge);
    }

    public Fido2AssertionOutcome? CompleteAssertion(string optionsJson, string assertionResponseJson, IReadOnlyList<Fido2AssertionCandidate> candidates)
    {
        var options = AssertionOptions.FromJson(optionsJson);
        var assertionResponse = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionResponseJson)
            ?? throw new ArgumentException("The assertion response is invalid.", nameof(assertionResponseJson));

        var matched = candidates.SingleOrDefault(candidate => candidate.CredentialId.AsSpan().SequenceEqual(assertionResponse.RawId));
        if (matched is null)
        {
            logger.LogWarning("CompleteAssertion received a CredentialId {CredentialId} that does not match any allowed candidate.", Convert.ToBase64String(assertionResponse.RawId));
            return null;
        }

        try
        {
            var result = fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertionResponse,
                OriginalOptions = options,
                StoredPublicKey = matched.PublicKey,
                StoredSignatureCounter = (uint)matched.SignatureCounter,
                IsUserHandleOwnerOfCredentialIdCallback = (_, _) => Task.FromResult(true)
            }).GetAwaiter().GetResult();

            if (result.SignCount != 0 && result.SignCount <= matched.SignatureCounter)
            {
                // A returned counter that does not exceed the stored value never advances -
                // treated as a replay/clone, even if the library itself accepted the assertion.
                // SignCount == 0 is excluded: WebAuthn Level 2 6.1.3 explicitly permits authenticators to
                // never implement a counter and always report 0 (e.g. Apple's synced/platform passkeys on
                // iOS/macOS) - without this exclusion every assertion from such an authenticator regresses
                // 0 <= 0 and is rejected as a clone, even though nothing was cloned.
                logger.LogWarning("CompleteAssertion rejected CredentialId {CredentialId}: returned SignCount {SignCount} did not exceed the stored SignatureCounter {StoredSignatureCounter} - possible cloned/replayed FIDO2 credential.",
                    Convert.ToBase64String(matched.CredentialId), result.SignCount, matched.SignatureCounter);
                return null;
            }

            return new Fido2AssertionOutcome(matched.CredentialId, result.SignCount);
        }
        catch (Fido2VerificationException ex)
        {
            logger.LogWarning(ex, "CompleteAssertion failed FIDO2 assertion verification for CredentialId {CredentialId}.", Convert.ToBase64String(matched.CredentialId));
            return null;
        }
    }
}
