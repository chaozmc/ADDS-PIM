using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Mfa;

public enum TotpTransactionVerificationOutcome
{
    Succeeded,
    TransactionNotFound,
    TransactionExpired,
    FactorNotAllowed,
    NoActiveFactor,
    Locked,
    InvalidCode
}

public sealed record VerifyTotpTransactionCommand(Guid RequestId, Guid PersonId, string Code, MembershipRequestTransitionAuditContext AuditContext);

/// <summary>On <see cref="TotpTransactionVerificationOutcome.Succeeded"/>, carries the transaction's bound tuple so the caller can continue execution without the client re-submitting it.</summary>
public sealed record VerifyTotpTransactionResult(
    TotpTransactionVerificationOutcome Outcome,
    Guid TargetAccountId = default,
    Guid TargetGroupId = default,
    long RequestedTtlSeconds = default,
    string? TicketReference = null);

/// <summary>
/// Verifies a TOTP code against the pending mfa-transaction-v1 challenge for a membership request
/// and, on success, transitions the request AwaitingSecondFactor -&gt; SecondFactorValidated.
/// </summary>
public sealed class VerifyTotpTransactionUseCase(
    IMfaTransactionStore transactionStore,
    ITotpVerificationStore totpStore,
    ITotpSecretProtector protector,
    IMembershipRequestStateStore stateStore,
    TimeProvider timeProvider)
{
    public async Task<VerifyTotpTransactionResult> ExecuteAsync(VerifyTotpTransactionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.RequestId == Guid.Empty || command.PersonId == Guid.Empty)
        {
            throw new ArgumentException("A request ID and person ID are required.", nameof(command));
        }

        var now = timeProvider.GetUtcNow();
        var transaction = await transactionStore.FindPendingAsync(command.RequestId, command.PersonId, cancellationToken);
        if (transaction is null)
        {
            return new(TotpTransactionVerificationOutcome.TransactionNotFound);
        }

        if (now >= transaction.ExpiresUtc)
        {
            await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.Expired, command.AuditContext with { FailureCategory = "Mfa" }, "The MFA transaction expired before a factor was confirmed.", cancellationToken);
            return new(TotpTransactionVerificationOutcome.TransactionExpired);
        }

        if (!transaction.AllowedFactorTypes.HasFlag(SecondFactorType.Totp))
        {
            await totpStore.RecordRejectionAsync(command.PersonId, command.RequestId, "TotpVerificationRejectedFactorNotAllowed", now, command.AuditContext, cancellationToken);
            return new(TotpTransactionVerificationOutcome.FactorNotAllowed);
        }

        var factor = await totpStore.FindActiveAsync(command.PersonId, cancellationToken);
        if (factor is null)
        {
            await totpStore.RecordRejectionAsync(command.PersonId, command.RequestId, "TotpVerificationRejectedNoActiveFactor", now, command.AuditContext, cancellationToken);
            return new(TotpTransactionVerificationOutcome.NoActiveFactor);
        }

        if (factor.LockedUntilUtc is { } lockedUntilUtc && now < lockedUntilUtc)
        {
            await totpStore.RecordRejectionAsync(command.PersonId, command.RequestId, "TotpVerificationRejectedLocked", now, command.AuditContext, cancellationToken);
            return new(TotpTransactionVerificationOutcome.Locked);
        }

        var secret = protector.Unprotect(factor.EncryptedSecret, factor.ProtectionKeyId);
        var codeIsValid = Totp.TryValidate(secret, command.Code, now, out var timeStep) && timeStep > (factor.LastUsedTimeStep ?? long.MinValue);
        if (!codeIsValid)
        {
            var failure = await totpStore.RecordFailureAsync(factor.TotpFactorId, command.PersonId, transaction.MfaTransactionId, command.RequestId, now, command.AuditContext, cancellationToken);
            return new(failure.LockedUntilUtc is not null ? TotpTransactionVerificationOutcome.Locked : TotpTransactionVerificationOutcome.InvalidCode);
        }

        await totpStore.RecordSuccessAsync(factor.TotpFactorId, command.PersonId, transaction.MfaTransactionId, command.RequestId, timeStep, now, command.AuditContext, cancellationToken);
        await transactionStore.TryConsumeAsync(transaction.MfaTransactionId, SecondFactorType.Totp, now, cancellationToken);
        await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.SecondFactorValidated, command.AuditContext, "TOTP second factor verified.", cancellationToken);
        return new(TotpTransactionVerificationOutcome.Succeeded, transaction.TargetAccountId, transaction.TargetGroupId, transaction.RequestedTtlSeconds, transaction.TicketReference);
    }
}
