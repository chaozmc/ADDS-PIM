using ADDS.PIM.Application.Authorization;

namespace ADDS.PIM.Application.Mfa;

/// <summary>Onboarding status for the current person's second factors, for display (e.g. a header badge) only.</summary>
public sealed record MyMfaStatus(
    bool TotpEnrolled,
    DateTimeOffset? TotpConfirmedUtc,
    bool Fido2Available,
    bool Fido2Enrolled,
    DateTimeOffset? Fido2ConfirmedUtc,
    IReadOnlyList<Fido2CredentialDisplay> Fido2Credentials);

public sealed class GetMyMfaStatusUseCase(ResolveCurrentPersonUseCase resolvePerson, IMfaStatusStore store)
{
    public async Task<MyMfaStatus?> ExecuteAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
    {
        var personId = await resolvePerson.ExecuteAsync(actor, cancellationToken);
        if (personId is null)
        {
            return null;
        }

        var totp = await store.FindTotpStatusAsync(personId.Value, cancellationToken);
        var fido2 = await store.FindFido2StatusAsync(personId.Value, cancellationToken);
        var earliestCredential = fido2.Credentials.Count == 0 ? null : fido2.Credentials.MinBy(credential => credential.CreatedUtc);
        return new MyMfaStatus(totp.Enrolled, totp.ConfirmedUtc, Fido2Available: true, fido2.Enrolled, earliestCredential?.CreatedUtc, fido2.Credentials);
    }
}
