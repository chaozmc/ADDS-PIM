namespace ADDS.PIM.Application.Mfa;

public sealed record StartFido2RegistrationCommand(
    Guid PersonId,
    string PersonDisplayName,
    Guid CorrelationId,
    string FrontendClientId,
    string? SourceIpAddress,
    string? ClientSourceIpAddress,
    Guid? StepUpChallengeId,
    string? StepUpTotpCode,
    string? StepUpAssertionResponseJson);

/// <summary>
/// Either the actual WebAuthn registration challenge (bootstrap case, or step-up already satisfied) or
/// a step-up requirement (an active factor already exists and no valid proof was supplied yet).
/// </summary>
public sealed record Fido2RegistrationStartResult(
    bool StepUpRequired,
    bool StepUpTotpAllowed,
    Guid? StepUpChallengeId,
    string? StepUpAssertionOptionsJson,
    Guid? RegistrationChallengeId,
    string? RegistrationOptionsJson);

/// <summary>
/// Starts a FIDO2 credential registration (plus this deployment's decision to allow multiple
/// credentials per person). The very first second factor ever (no active TOTP, no active FIDO2
/// credential) may be registered on Windows authentication alone, exactly like TOTP's bootstrap case.
/// Every registration beyond that requires proving an already-active factor first.
/// </summary>
public sealed class StartFido2RegistrationUseCase(
    ITotpVerificationStore totpStore,
    ITotpSecretProtector totpProtector,
    IFido2CredentialStore credentialStore,
    IFido2ChallengeStore challengeStore,
    IFido2RegistrationCeremony registrationCeremony,
    IFido2AssertionCeremony assertionCeremony,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    public async Task<Fido2RegistrationStartResult> ExecuteAsync(StartFido2RegistrationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = timeProvider.GetUtcNow();
        var activeTotp = await totpStore.FindActiveAsync(command.PersonId, cancellationToken);
        var activeCredentials = await credentialStore.ListActiveAsync(command.PersonId, cancellationToken);
        var hasAnyActiveFactor = activeTotp is not null || activeCredentials.Count > 0;

        if (!hasAnyActiveFactor)
        {
            return await IssueRegistrationChallengeAsync(command.PersonId, command.PersonDisplayName, [], now, cancellationToken);
        }

        var stepUpSatisfied = await TryConsumeStepUpAsync(command, activeTotp, activeCredentials, now, cancellationToken);
        if (stepUpSatisfied)
        {
            return await IssueRegistrationChallengeAsync(
                command.PersonId, command.PersonDisplayName, activeCredentials.Select(credential => credential.CredentialId).ToArray(), now, cancellationToken);
        }

        string? assertionOptionsJson = null;
        if (activeCredentials.Count > 0)
        {
            assertionOptionsJson = assertionCeremony.BeginAssertion(activeCredentials.Select(credential => credential.CredentialId).ToArray()).OptionsJson;
        }

        var newChallengeId = await challengeStore.CreateAsync(command.PersonId, Fido2ChallengePurpose.StepUp, assertionOptionsJson ?? string.Empty, now, now.Add(ChallengeLifetime), cancellationToken);
        return new Fido2RegistrationStartResult(StepUpRequired: true, activeTotp is not null, newChallengeId, assertionOptionsJson, null, null);
    }

    private async Task<bool> TryConsumeStepUpAsync(
        StartFido2RegistrationCommand command,
        ActiveTotpFactor? activeTotp,
        IReadOnlyList<ActiveFido2Credential> activeCredentials,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (command.StepUpChallengeId is not { } challengeId)
        {
            return false;
        }

        var pending = await challengeStore.FindPendingAsync(challengeId, command.PersonId, Fido2ChallengePurpose.StepUp, cancellationToken);
        if (pending is null || now >= pending.ExpiresUtc)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(command.StepUpTotpCode) && activeTotp is not null)
        {
            if (activeTotp.LockedUntilUtc is { } lockedUntilUtc && now < lockedUntilUtc)
            {
                return false;
            }

            var secret = totpProtector.Unprotect(activeTotp.EncryptedSecret, activeTotp.ProtectionKeyId);
            var codeIsValid = Totp.TryValidate(secret, command.StepUpTotpCode, now, out var timeStep) && timeStep > (activeTotp.LastUsedTimeStep ?? long.MinValue);
            if (!codeIsValid)
            {
                await totpStore.RecordStepUpFailureAsync(activeTotp.TotpFactorId, command.PersonId, now, command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
                return false;
            }

            await totpStore.RecordStepUpSuccessAsync(activeTotp.TotpFactorId, command.PersonId, timeStep, now, command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
            return await challengeStore.TryConsumeAsync(challengeId, Domain.Security.SecondFactorType.Totp, now, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(command.StepUpAssertionResponseJson) && activeCredentials.Count > 0)
        {
            var candidates = activeCredentials.Select(credential => new Fido2AssertionCandidate(credential.CredentialId, credential.PublicKey, credential.SignatureCounter)).ToArray();
            var outcome = assertionCeremony.CompleteAssertion(pending.OptionsJson, command.StepUpAssertionResponseJson, candidates);
            if (outcome is null)
            {
                await credentialStore.RecordStepUpFailureAsync(command.PersonId, now, command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
                return false;
            }

            var matched = activeCredentials.Single(credential => credential.CredentialId.AsSpan().SequenceEqual(outcome.CredentialId));
            await credentialStore.RecordStepUpSuccessAsync(matched.Fido2CredentialId, command.PersonId, outcome.NewSignatureCounter, now, command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
            return await challengeStore.TryConsumeAsync(challengeId, Domain.Security.SecondFactorType.Fido2, now, cancellationToken);
        }

        return false;
    }

    private async Task<Fido2RegistrationStartResult> IssueRegistrationChallengeAsync(Guid personId, string personDisplayName, IReadOnlyList<byte[]> excludeCredentialIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var options = registrationCeremony.BeginRegistration(personId, personDisplayName, excludeCredentialIds);
        var challengeId = await challengeStore.CreateAsync(personId, Fido2ChallengePurpose.Registration, options.OptionsJson, now, now.Add(ChallengeLifetime), cancellationToken);
        return new Fido2RegistrationStartResult(StepUpRequired: false, StepUpTotpAllowed: false, null, null, challengeId, options.OptionsJson);
    }
}
