using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class TotpFactorEntity
{
    public Guid TotpFactorId { get; set; }
    public Guid PersonId { get; set; }
    public required byte[] EncryptedSecret { get; set; }
    public required string ProtectionKeyId { get; set; }
    public DateTimeOffset EnrolledUtc { get; set; }
    public DateTimeOffset EnrollmentExpiresUtc { get; set; }
    public DateTimeOffset? ConfirmedUtc { get; set; }
    public bool IsActive { get; set; }
    public long? LastUsedTimeStep { get; set; }
    public int ConsecutiveFailedAttempts { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? RevokedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TotpUsedTimeStepEntity
{
    public Guid TotpFactorId { get; set; }
    public long TimeStep { get; set; }
    public DateTimeOffset UsedUtc { get; set; }
    public Guid MfaTransactionId { get; set; }
}

public sealed class Fido2CredentialEntity
{
    public Guid Fido2CredentialId { get; set; }
    public Guid PersonId { get; set; }
    public required byte[] CredentialId { get; set; }
    public required byte[] PublicKey { get; set; }
    public long SignatureCounter { get; set; }
    public string? Aaguid { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? RevokedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// A one-use, short-lived FIDO2 challenge not tied to a membership request: either a step-up proof
/// (that the person already controls an active factor, before a further credential may be registered)
/// or the actual WebAuthn registration challenge for a new credential. <see cref="Purpose"/>
/// distinguishes the two; both share the same lifecycle shape as <see cref="MfaTransactionEntity"/>.
/// </summary>
public sealed class Fido2ChallengeEntity
{
    public Guid ChallengeId { get; set; }
    public Guid PersonId { get; set; }
    public required string Purpose { get; set; }
    public required byte[] Challenge { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
    public SecondFactorType? SatisfiedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class MfaTransactionEntity
{
    public Guid MfaTransactionId { get; set; }
    public Guid RequestId { get; set; }
    public Guid PersonId { get; set; }
    public Guid ActorAccountId { get; set; }
    public Guid TargetAccountId { get; set; }
    public Guid TargetGroupId { get; set; }
    public long RequestedTtlSeconds { get; set; }
    public required string PolicyRequirementsSummary { get; set; }
    public SecondFactorType AllowedFactorTypes { get; set; }
    public required string TransactionHash { get; set; }
    public byte[]? Fido2Challenge { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
    public SecondFactorType? SatisfiedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
