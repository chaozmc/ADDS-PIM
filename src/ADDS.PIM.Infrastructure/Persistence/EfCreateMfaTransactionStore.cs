using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Infrastructure.Persistence.Entities;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>
/// Composes the Created -&gt; AwaitingSecondFactor transition (via <see cref="IMembershipRequestStateStore"/>,
/// which performs its own SaveChangesAsync) with the MfaTransaction insert in one explicit database
/// transaction, so a request is never left in AwaitingSecondFactor without its bound challenge row.
/// </summary>
public sealed class EfCreateMfaTransactionStore(PimDbContext dbContext, IMembershipRequestStateStore stateStore) : ICreateMfaTransactionStore
{
    public async Task CreateAsync(NewMfaTransaction transaction, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await stateStore.TransitionAsync(
            transaction.RequestId,
            MembershipRequestStatus.Created,
            MembershipRequestStatus.AwaitingSecondFactor,
            transaction.AuditContext,
            "Second factor required by policy.",
            cancellationToken);

        dbContext.MfaTransactions.Add(new MfaTransactionEntity
        {
            MfaTransactionId = transaction.MfaTransactionId,
            RequestId = transaction.RequestId,
            PersonId = transaction.PersonId,
            ActorAccountId = transaction.ActorAccountId,
            TargetAccountId = transaction.TargetAccountId,
            TargetGroupId = transaction.TargetGroupId,
            RequestedTtlSeconds = transaction.RequestedTtlSeconds,
            PolicyRequirementsSummary = transaction.PolicyRequirementsSummary,
            AllowedFactorTypes = transaction.AllowedFactorTypes,
            TransactionHash = transaction.TransactionHash,
            Fido2Challenge = transaction.Fido2OptionsJson is null ? null : System.Text.Encoding.UTF8.GetBytes(transaction.Fido2OptionsJson),
            CreatedUtc = transaction.CreatedUtc,
            ExpiresUtc = transaction.ExpiresUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbTransaction.CommitAsync(cancellationToken);
    }
}
