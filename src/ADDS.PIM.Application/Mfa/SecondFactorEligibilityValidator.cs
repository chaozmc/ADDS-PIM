using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Mfa;

/// <summary>
/// Checks, at request-submission time, whether the person has at least one active factor among
/// the policy's allowed second-factor types - so a request that could never be completed (no
/// eligible factor at all) is rejected immediately instead of sitting in AwaitingSecondFactor
/// until its transaction window lapses (poc-status-and-handoff, "Known MFA gap").
/// </summary>
public sealed class SecondFactorEligibilityValidator(ITotpVerificationStore totpStore, IFido2CredentialStore fido2Store)
{
    public async Task<bool> HasEligibleFactorAsync(Guid personId, SecondFactorType allowedFactorTypes, CancellationToken cancellationToken)
    {
        if (allowedFactorTypes.HasFlag(SecondFactorType.Totp) && await totpStore.FindActiveAsync(personId, cancellationToken) is not null)
        {
            return true;
        }

        if (allowedFactorTypes.HasFlag(SecondFactorType.Fido2) && await fido2Store.CountActiveAsync(personId, cancellationToken) > 0)
        {
            return true;
        }

        return false;
    }
}
