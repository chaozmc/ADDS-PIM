using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Mfa;

public sealed record CreateMfaTransactionCommand(
    Guid RequestId,
    Guid PersonId,
    Guid ActorAccountId,
    Guid TargetAccountId,
    Guid TargetGroupId,
    long RequestedTtlSeconds,
    string PolicyRequirementsSummary,
    SecondFactorType AllowedFactorTypes,
    MembershipRequestTransitionAuditContext AuditContext);

public sealed record MfaTransactionCreated(Guid MfaTransactionId, DateTimeOffset ExpiresUtc);

/// <summary>
/// Creates the one-use, 5-minute mfa-transaction-v1 challenge bound to a just-created
/// membership request whose policy requires a second factor, transitioning the request to
/// AwaitingSecondFactor in the same operation.
/// </summary>
public sealed class CreateMfaTransactionUseCase(ICreateMfaTransactionStore store, IFido2CredentialStore fido2Store, IFido2AssertionCeremony assertionCeremony, TimeProvider timeProvider)
{
    private static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(5);

    public async Task<MfaTransactionCreated> ExecuteAsync(CreateMfaTransactionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.AllowedFactorTypes.IsValidPolicyValue())
        {
            throw new ArgumentException("A policy requiring a second factor must specify a valid allowed-factor set.", nameof(command));
        }

        var now = timeProvider.GetUtcNow();
        var expiresUtc = now.Add(TransactionLifetime);
        var mfaTransactionId = Guid.NewGuid();
        var transactionHash = MfaTransactionCanonicalizer.ComputeHash(
            command.RequestId, command.PersonId, command.ActorAccountId, command.TargetAccountId, command.TargetGroupId, command.RequestedTtlSeconds);

        string? fido2OptionsJson = null;
        if (command.AllowedFactorTypes.HasFlag(SecondFactorType.Fido2))
        {
            var activeCredentials = await fido2Store.ListActiveAsync(command.PersonId, cancellationToken);
            if (activeCredentials.Count > 0)
            {
                fido2OptionsJson = assertionCeremony.BeginAssertion(activeCredentials.Select(credential => credential.CredentialId).ToArray()).OptionsJson;
            }
        }

        await store.CreateAsync(
            new NewMfaTransaction(
                mfaTransactionId,
                command.RequestId,
                command.PersonId,
                command.ActorAccountId,
                command.TargetAccountId,
                command.TargetGroupId,
                command.RequestedTtlSeconds,
                command.PolicyRequirementsSummary,
                command.AllowedFactorTypes,
                transactionHash,
                fido2OptionsJson,
                now,
                expiresUtc,
                command.AuditContext),
            cancellationToken);

        return new MfaTransactionCreated(mfaTransactionId, expiresUtc);
    }
}
