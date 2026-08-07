namespace ADDS.PIM.Application.Mfa;

public sealed record CompleteFido2RegistrationCommand(
    Guid PersonId,
    Guid ChallengeId,
    string AttestationResponseJson,
    string? Label,
    Guid CorrelationId,
    string FrontendClientId,
    string? SourceIpAddress,
    string? ClientSourceIpAddress);

public enum Fido2RegistrationCompletionOutcome
{
    Succeeded,
    ChallengeNotFound,
    ChallengeExpired,
    CeremonyRejected
}

public sealed record Fido2RegistrationCompletionResult(Fido2RegistrationCompletionOutcome Outcome, Guid Fido2CredentialId = default);

/// <summary>
/// Completes a FIDO2 credential registration ceremony. Unlike TOTP, no separate confirm-with-code step
/// is needed - the ceremony itself (origin/RP ID/challenge match, attestation, user verification) proves
/// possession, so the credential is active immediately.
/// </summary>
public sealed class CompleteFido2RegistrationUseCase(
    IFido2ChallengeStore challengeStore,
    IFido2CredentialStore credentialStore,
    IFido2RegistrationCeremony registrationCeremony,
    TimeProvider timeProvider)
{
    public async Task<Fido2RegistrationCompletionResult> ExecuteAsync(CompleteFido2RegistrationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = timeProvider.GetUtcNow();
        var pending = await challengeStore.FindPendingAsync(command.ChallengeId, command.PersonId, Fido2ChallengePurpose.Registration, cancellationToken);
        if (pending is null)
        {
            return new(Fido2RegistrationCompletionOutcome.ChallengeNotFound);
        }

        if (now >= pending.ExpiresUtc)
        {
            return new(Fido2RegistrationCompletionOutcome.ChallengeExpired);
        }

        var result = registrationCeremony.CompleteRegistration(pending.OptionsJson, command.AttestationResponseJson);
        if (result is null)
        {
            return new(Fido2RegistrationCompletionOutcome.CeremonyRejected);
        }

        if (!await challengeStore.TryConsumeAsync(command.ChallengeId, Domain.Security.SecondFactorType.Fido2, now, cancellationToken))
        {
            return new(Fido2RegistrationCompletionOutcome.ChallengeExpired);
        }

        var credentialId = Guid.NewGuid();
        var label = string.IsNullOrWhiteSpace(command.Label) ? null : command.Label.Trim();
        await credentialStore.CreateAsync(
            new NewStoredFido2Credential(credentialId, command.PersonId, result.CredentialId, result.PublicKey, result.SignatureCounter, result.Aaguid, label, now),
            command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);

        return new(Fido2RegistrationCompletionOutcome.Succeeded, credentialId);
    }
}
