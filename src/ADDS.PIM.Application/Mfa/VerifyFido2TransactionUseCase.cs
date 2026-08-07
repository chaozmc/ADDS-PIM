using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Mfa;

public enum Fido2TransactionVerificationOutcome
{
    Succeeded,
    TransactionNotFound,
    TransactionExpired,
    FactorNotAllowed,
    NoActiveCredential,
    Rejected
}

public sealed record VerifyFido2TransactionCommand(Guid RequestId, Guid PersonId, string AssertionResponseJson, MembershipRequestTransitionAuditContext AuditContext);

/// <summary>On <see cref="Fido2TransactionVerificationOutcome.Succeeded"/>, carries the transaction's bound tuple so the caller can continue execution without the client re-submitting it.</summary>
public sealed record VerifyFido2TransactionResult(
    Fido2TransactionVerificationOutcome Outcome,
    Guid TargetAccountId = default,
    Guid TargetGroupId = default,
    long RequestedTtlSeconds = default,
    string? TicketReference = null);

/// <summary>
/// Verifies a FIDO2 assertion against the pending mfa-transaction-v1 challenge for a membership request
/// and, on success, transitions the request AwaitingSecondFactor -&gt; SecondFactorValidated.
/// Mirrors <see cref="VerifyTotpTransactionUseCase"/>.
/// </summary>
public sealed class VerifyFido2TransactionUseCase(
    IMfaTransactionStore transactionStore,
    IFido2CredentialStore credentialStore,
    IFido2VerificationStore verificationStore,
    IFido2AssertionCeremony assertionCeremony,
    IMembershipRequestStateStore stateStore,
    TimeProvider timeProvider)
{
    public async Task<VerifyFido2TransactionResult> ExecuteAsync(VerifyFido2TransactionCommand command, CancellationToken cancellationToken)
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
            return new(Fido2TransactionVerificationOutcome.TransactionNotFound);
        }

        if (now >= transaction.ExpiresUtc)
        {
            await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.Expired, command.AuditContext with { FailureCategory = "Mfa" }, "The MFA transaction expired before a factor was confirmed.", cancellationToken);
            return new(Fido2TransactionVerificationOutcome.TransactionExpired);
        }

        if (!transaction.AllowedFactorTypes.HasFlag(SecondFactorType.Fido2))
        {
            await verificationStore.RecordRejectionAsync(command.PersonId, command.RequestId, "Fido2AssertionRejectedFactorNotAllowed", now, command.AuditContext, cancellationToken);
            return new(Fido2TransactionVerificationOutcome.FactorNotAllowed);
        }

        var activeCredentials = await credentialStore.ListActiveAsync(command.PersonId, cancellationToken);
        if (activeCredentials.Count == 0 || transaction.Fido2OptionsJson is null)
        {
            await verificationStore.RecordRejectionAsync(command.PersonId, command.RequestId, "Fido2AssertionRejectedNoActiveCredential", now, command.AuditContext, cancellationToken);
            return new(Fido2TransactionVerificationOutcome.NoActiveCredential);
        }

        var candidates = activeCredentials.Select(credential => new Fido2AssertionCandidate(credential.CredentialId, credential.PublicKey, credential.SignatureCounter)).ToArray();
        var outcome = assertionCeremony.CompleteAssertion(transaction.Fido2OptionsJson, command.AssertionResponseJson, candidates);
        if (outcome is null)
        {
            await verificationStore.RecordRejectionAsync(command.PersonId, command.RequestId, "Fido2AssertionRejectedCounterRegression", now, command.AuditContext, cancellationToken);
            return new(Fido2TransactionVerificationOutcome.Rejected);
        }

        var matched = activeCredentials.Single(credential => credential.CredentialId.AsSpan().SequenceEqual(outcome.CredentialId));
        await verificationStore.RecordSuccessAsync(matched.Fido2CredentialId, command.PersonId, outcome.NewSignatureCounter, command.RequestId, now, command.AuditContext, cancellationToken);
        await transactionStore.TryConsumeAsync(transaction.MfaTransactionId, SecondFactorType.Fido2, now, cancellationToken);
        await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.SecondFactorValidated, command.AuditContext, "FIDO2 second factor verified.", cancellationToken);
        return new(Fido2TransactionVerificationOutcome.Succeeded, transaction.TargetAccountId, transaction.TargetGroupId, transaction.RequestedTtlSeconds, transaction.TicketReference);
    }
}
