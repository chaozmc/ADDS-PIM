namespace ADDS.PIM.Application.Mfa;

public sealed record TotpStatus(bool Enrolled, DateTimeOffset? ConfirmedUtc);

public sealed record Fido2CredentialDisplay(Guid Fido2CredentialId, string? Label, DateTimeOffset CreatedUtc);

public sealed record Fido2Status(bool Enrolled, IReadOnlyList<Fido2CredentialDisplay> Credentials);

/// <summary>
/// Read-only MFA onboarding status for display purposes only. Deliberately separate from
/// <see cref="ITotpVerificationStore"/>/<see cref="IFido2CredentialStore"/>, which are scoped to the
/// security-critical verification path (anti-replay, lockout) - this store has no bearing on any
/// authorization or verification decision.
/// </summary>
public interface IMfaStatusStore
{
    Task<TotpStatus> FindTotpStatusAsync(Guid personId, CancellationToken cancellationToken);

    Task<Fido2Status> FindFido2StatusAsync(Guid personId, CancellationToken cancellationToken);
}
