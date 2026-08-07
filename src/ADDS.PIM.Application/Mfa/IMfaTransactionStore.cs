using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Mfa;

/// <summary>A pending, unconsumed mfa-transaction-v1 row. Callers must separately check <see cref="ExpiresUtc"/>.</summary>
public sealed record PendingMfaTransaction(
    Guid MfaTransactionId,
    Guid RequestId,
    Guid PersonId,
    Guid ActorAccountId,
    Guid TargetAccountId,
    Guid TargetGroupId,
    long RequestedTtlSeconds,
    SecondFactorType AllowedFactorTypes,
    DateTimeOffset ExpiresUtc,
    string? TicketReference = null,
    string? Fido2OptionsJson = null);

public interface IMfaTransactionStore
{
    /// <summary>Returns null when no matching, unconsumed transaction exists for this request/person &mdash; regardless of whether it never existed or belongs to a different person, to avoid leaking which case applies.</summary>
    Task<PendingMfaTransaction?> FindPendingAsync(Guid requestId, Guid personId, CancellationToken cancellationToken);

    /// <summary>Atomically marks the transaction consumed; false if it was already consumed or has since expired.</summary>
    Task<bool> TryConsumeAsync(Guid mfaTransactionId, SecondFactorType satisfiedBy, DateTimeOffset consumedUtc, CancellationToken cancellationToken);
}
