using ADDS.PIM.Application.MembershipRequests;

namespace ADDS.PIM.Application.Mfa;

/// <summary>An active (confirmed, non-revoked) TOTP factor, as needed to verify a code against it.</summary>
public sealed record ActiveTotpFactor(
    Guid TotpFactorId,
    Guid PersonId,
    byte[] EncryptedSecret,
    string ProtectionKeyId,
    long? LastUsedTimeStep,
    DateTimeOffset? LockedUntilUtc);

public sealed record TotpFailureOutcome(int ConsecutiveFailedAttempts, DateTimeOffset? LockedUntilUtc);

/// <summary>
/// Verification-time TOTP state, distinct from <see cref="ITotpFactorEnrollmentStore"/>/<see cref="ITotpEnrollmentConfirmationStore"/>:
/// this store reads the already-active factor and writes the anti-replay ledger and lockout state.
/// </summary>
public interface ITotpVerificationStore
{
    Task<ActiveTotpFactor?> FindActiveAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>Records the used time step (anti-replay) and resets the failure counter, auditing the success.</summary>
    Task RecordSuccessAsync(Guid totpFactorId, Guid personId, Guid mfaTransactionId, Guid requestId, long timeStep, DateTimeOffset usedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken);

    /// <summary>Atomically increments the failure counter, locking the factor for 15 minutes on the 5th consecutive failure, and audits the outcome.</summary>
    Task<TotpFailureOutcome> RecordFailureAsync(Guid totpFactorId, Guid personId, Guid mfaTransactionId, Guid requestId, DateTimeOffset failedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken);

    /// <summary>Audits a rejected verification attempt that never reached a code comparison (no active factor, factor not allowed by policy, or already locked).</summary>
    Task RecordRejectionAsync(Guid personId, Guid requestId, string eventType, DateTimeOffset occurredUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful TOTP step-up (proving an already-active TOTP factor before a further FIDO2
    /// credential may be enrolled). Shares the same anti-replay floor (<c>LastUsedTimeStep</c>) as
    /// request-bound verification, since a TOTP time step is single-use across all uses -
    /// not tied to a membership request, so no <see cref="MembershipRequestTransitionAuditContext"/>.
    /// </summary>
    Task RecordStepUpSuccessAsync(Guid totpFactorId, Guid personId, long timeStep, DateTimeOffset usedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken);

    /// <summary>Atomically increments the failure counter (same lockout rule as request-bound verification) and audits the rejected step-up attempt.</summary>
    Task<TotpFailureOutcome> RecordStepUpFailureAsync(Guid totpFactorId, Guid personId, DateTimeOffset failedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken);
}
