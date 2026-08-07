namespace ADDS.PIM.Application.Worker;

public sealed record TemporaryGroupMembershipOperation(
    Guid TargetAccountObjectGuid,
    Guid TargetGroupObjectGuid,
    long RequestedTtlSeconds);

public enum TemporaryGroupMembershipResultKind
{
    Verified,
    ExistingMembership,
    VerificationFailed,
    ActiveDirectoryFailure,
    PowerShellFailure
}

public sealed record TemporaryGroupMembershipResult(
    TemporaryGroupMembershipResultKind Kind,
    string? DomainController,
    long? RemainingTtlSeconds,
    string? ErrorCode);

public interface ITemporaryGroupMembershipService
{
    Task<TemporaryGroupMembershipResult> AddAndVerifyAsync(
        TemporaryGroupMembershipOperation operation,
        CancellationToken cancellationToken);
}
