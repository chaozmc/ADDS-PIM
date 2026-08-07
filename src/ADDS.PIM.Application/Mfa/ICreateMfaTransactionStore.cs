using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Mfa;

public sealed record NewMfaTransaction(
    Guid MfaTransactionId,
    Guid RequestId,
    Guid PersonId,
    Guid ActorAccountId,
    Guid TargetAccountId,
    Guid TargetGroupId,
    long RequestedTtlSeconds,
    string PolicyRequirementsSummary,
    SecondFactorType AllowedFactorTypes,
    string TransactionHash,
    // The serialized FIDO2 AssertionOptions when AllowedFactorTypes includes FIDO2 and the
    // person has at least one active credential at transaction-creation time; otherwise null.
    string? Fido2OptionsJson,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    MembershipRequestTransitionAuditContext AuditContext);

/// <summary>
/// Atomically transitions the membership request Created -&gt; AwaitingSecondFactor and persists the
/// bound mfa-transaction-v1 row in the same unit of work.
/// </summary>
public interface ICreateMfaTransactionStore
{
    Task CreateAsync(NewMfaTransaction transaction, CancellationToken cancellationToken);
}
