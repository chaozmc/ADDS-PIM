namespace ADDS.PIM.Contracts.Mfa.V1;

/// <summary>Versioned Web-to-API request body for starting a TOTP enrollment for the current person.</summary>
public sealed record EnrollTotpFactorRequest(Guid ActorDirectoryScopeId, Guid ActorObjectGuid);

/// <summary>
/// The raw secret is embedded in <see cref="ProvisioningUri"/> and repeated in base32 form as
/// <see cref="Secret"/> (for manual entry when scanning isn't possible) only in this one response;
/// it is never returned again and must never be logged or persisted by the caller.
/// </summary>
public sealed record TotpEnrollmentStarted(Guid TotpFactorId, string ProvisioningUri, string Secret, string ProtectionKeyId, DateTimeOffset ExpiresUtc);

public sealed record ConfirmTotpEnrollmentRequest(Guid ActorDirectoryScopeId, Guid ActorObjectGuid, string Code);

/// <summary>Versioned Web-to-API request body for the transaction-bound second-factor confirmation of a pending membership request.</summary>
public sealed record VerifyTotpSecondFactorRequest(Guid ActorDirectoryScopeId, Guid ActorObjectGuid, string Code);

/// <summary>Returned instead of MembershipRequestAccepted when a create call results in AwaitingSecondFactor.</summary>
public sealed record MembershipRequestPendingSecondFactor(Guid RequestId, Guid CorrelationId, Guid MfaTransactionId, string Status, DateTimeOffset ExpiresUtc);

/// <summary>Versioned Web-to-API request body for the current person's MFA onboarding status (display only).</summary>
public sealed record QueryMyMfaStatus(Guid ActorDirectoryScopeId, Guid ActorObjectGuid);

public sealed record Fido2CredentialSummary(Guid Fido2CredentialId, string? Label, DateTimeOffset CreatedUtc);

public sealed record MyMfaStatusResponse(
    bool TotpEnrolled,
    DateTimeOffset? TotpConfirmedUtc,
    bool Fido2Available,
    bool Fido2Enrolled,
    DateTimeOffset? Fido2ConfirmedUtc,
    IReadOnlyList<Fido2CredentialSummary> Fido2Credentials);

/// <summary>
/// Starts a FIDO2 credential registration. The very first second factor ever may be
/// registered on Windows authentication alone; any registration after that requires proving an
/// already-active factor first via <see cref="StepUpChallengeId"/> plus <see cref="StepUpTotpCode"/>
/// or <see cref="StepUpAssertionResponseJson"/>.
/// </summary>
public sealed record StartFido2RegistrationRequest(
    Guid ActorDirectoryScopeId,
    Guid ActorObjectGuid,
    Guid? StepUpChallengeId,
    string? StepUpTotpCode,
    string? StepUpAssertionResponseJson);

/// <summary>
/// Either the actual WebAuthn <c>CredentialCreateOptions</c> (bootstrap case, or step-up already
/// satisfied) or a step-up requirement (an active factor already exists and no valid proof was
/// supplied yet) - never both.
/// </summary>
public sealed record Fido2RegistrationOptionsResponse(
    bool StepUpRequired,
    bool StepUpTotpAllowed,
    Guid? StepUpChallengeId,
    string? StepUpAssertionOptionsJson,
    Guid? RegistrationChallengeId,
    string? RegistrationOptionsJson);

public sealed record CompleteFido2RegistrationRequest(Guid ActorDirectoryScopeId, Guid ActorObjectGuid, Guid ChallengeId, string AttestationResponseJson, string? Label);

public sealed record Fido2AssertionOptionsQuery(Guid ActorDirectoryScopeId, Guid ActorObjectGuid);

public sealed record Fido2AssertionOptionsResponse(string OptionsJson, DateTimeOffset ExpiresUtc);

/// <summary>Versioned Web-to-API request body for the transaction-bound FIDO2 second-factor confirmation of a pending membership request.</summary>
public sealed record VerifyFido2SecondFactorRequest(Guid ActorDirectoryScopeId, Guid ActorObjectGuid, string AssertionResponseJson);
