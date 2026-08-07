using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Mfa;

public enum Fido2ChallengePurpose
{
    /// <summary>The actual WebAuthn "create a new credential" challenge, issued once any step-up requirement is met.</summary>
    Registration,

    /// <summary>Proof that the person already controls an active factor, required before registering any credential beyond the very first factor ever enrolled.</summary>
    StepUp
}

public sealed record PendingFido2Challenge(Guid ChallengeId, Guid PersonId, Fido2ChallengePurpose Purpose, string OptionsJson, DateTimeOffset ExpiresUtc);

/// <summary>
/// One-use, short-lived FIDO2 challenges not tied to a membership request (the mfa-transaction-v1
/// covers the request-bound case via <see cref="IMfaTransactionStore"/>; this store covers enrollment).
/// </summary>
public interface IFido2ChallengeStore
{
    Task<Guid> CreateAsync(Guid personId, Fido2ChallengePurpose purpose, string optionsJson, DateTimeOffset createdUtc, DateTimeOffset expiresUtc, CancellationToken cancellationToken);

    Task<PendingFido2Challenge?> FindPendingAsync(Guid challengeId, Guid personId, Fido2ChallengePurpose purpose, CancellationToken cancellationToken);

    /// <summary>Atomically marks the challenge consumed; false if it was already consumed or has since expired.</summary>
    Task<bool> TryConsumeAsync(Guid challengeId, SecondFactorType satisfiedBy, DateTimeOffset consumedUtc, CancellationToken cancellationToken);
}
