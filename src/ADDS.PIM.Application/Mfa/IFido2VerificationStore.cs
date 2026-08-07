using ADDS.PIM.Application.MembershipRequests;

namespace ADDS.PIM.Application.Mfa;

/// <summary>
/// Verification-time FIDO2 state for a membership-request second-factor confirmation, distinct from
/// <see cref="IFido2CredentialStore"/> (enrollment/admin/status): this store advances the anti-clone
/// signature counter and audits the outcome, mirroring <see cref="ITotpVerificationStore"/>'s split.
/// </summary>
public interface IFido2VerificationStore
{
    Task RecordSuccessAsync(Guid fido2CredentialId, Guid personId, long newSignatureCounter, Guid requestId, DateTimeOffset usedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken);

    /// <summary>Audits a rejected verification attempt (factor not allowed by policy, no active credential, ceremony rejection, or counter regression).</summary>
    Task RecordRejectionAsync(Guid personId, Guid requestId, string eventType, DateTimeOffset occurredUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken);
}
