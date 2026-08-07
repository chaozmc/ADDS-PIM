namespace ADDS.PIM.Application.Mfa;

public sealed record Fido2AssertionOptionsResult(bool TransactionFound, string? OptionsJson, DateTimeOffset ExpiresUtc = default);

/// <summary>Hands out the FIDO2 <c>AssertionOptions</c> already bound to a pending membership-request mfa-transaction-v1 challenge, for the client's <c>navigator.credentials.get()</c> call.</summary>
public sealed class GetFido2AssertionOptionsUseCase(IMfaTransactionStore transactionStore, TimeProvider timeProvider)
{
    public async Task<Fido2AssertionOptionsResult> ExecuteAsync(Guid requestId, Guid personId, CancellationToken cancellationToken)
    {
        var transaction = await transactionStore.FindPendingAsync(requestId, personId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (transaction is null || now >= transaction.ExpiresUtc || transaction.Fido2OptionsJson is null)
        {
            return new(false, null);
        }

        return new(true, transaction.Fido2OptionsJson, transaction.ExpiresUtc);
    }
}
