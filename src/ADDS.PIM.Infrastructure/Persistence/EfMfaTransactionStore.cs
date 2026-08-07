using System.Text;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfMfaTransactionStore(PimDbContext dbContext) : IMfaTransactionStore
{
    public async Task<PendingMfaTransaction?> FindPendingAsync(Guid requestId, Guid personId, CancellationToken cancellationToken)
    {
        var row = await (from transaction in dbContext.MfaTransactions.AsNoTracking()
                          join request in dbContext.MembershipRequests.AsNoTracking() on transaction.RequestId equals request.RequestId
                          where transaction.RequestId == requestId && transaction.PersonId == personId && transaction.ConsumedUtc == null
                          select new
                          {
                              transaction.MfaTransactionId,
                              transaction.RequestId,
                              transaction.PersonId,
                              transaction.ActorAccountId,
                              transaction.TargetAccountId,
                              transaction.TargetGroupId,
                              transaction.RequestedTtlSeconds,
                              transaction.AllowedFactorTypes,
                              transaction.ExpiresUtc,
                              request.TicketReference,
                              transaction.Fido2Challenge
                          }).SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new PendingMfaTransaction(
                row.MfaTransactionId, row.RequestId, row.PersonId, row.ActorAccountId, row.TargetAccountId, row.TargetGroupId,
                row.RequestedTtlSeconds, row.AllowedFactorTypes, row.ExpiresUtc, row.TicketReference,
                row.Fido2Challenge is null ? null : Encoding.UTF8.GetString(row.Fido2Challenge));
    }

    public async Task<bool> TryConsumeAsync(Guid mfaTransactionId, SecondFactorType satisfiedBy, DateTimeOffset consumedUtc, CancellationToken cancellationToken)
        => await dbContext.MfaTransactions
            .Where(x => x.MfaTransactionId == mfaTransactionId && x.ConsumedUtc == null && x.ExpiresUtc > consumedUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ConsumedUtc, consumedUtc)
                .SetProperty(x => x.SatisfiedBy, satisfiedBy), cancellationToken) == 1;
}
