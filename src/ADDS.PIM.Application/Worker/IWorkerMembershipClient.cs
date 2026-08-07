namespace ADDS.PIM.Application.Worker;

/// <summary>
/// Application-owned boundary from API orchestration to the isolated AD
/// Worker. Implementations create the versioned worker command; callers never
/// supply a worker URL, certificate value, LDAP input, or command hash.
/// </summary>
public interface IWorkerMembershipClient
{
    Task<WorkerMembershipDispatchResult> DispatchAsync(
        DispatchTemporaryGroupMembershipCommand command,
        CancellationToken cancellationToken);
}

public sealed record DispatchTemporaryGroupMembershipCommand(
    Guid RequestId,
    Guid CorrelationId,
    Guid DirectoryScopeId,
    Guid TargetAccountObjectGuid,
    Guid TargetGroupObjectGuid,
    long RequestedTtlSeconds);

public enum WorkerMembershipDispatchKind
{
    Completed,
    Rejected,
    TransportFailure,
    Timeout
}

public sealed record WorkerMembershipDispatchResult(
    WorkerMembershipDispatchKind Kind,
    TemporaryGroupMembershipResult? MembershipResult,
    int? HttpStatusCode,
    string? FailureCode);
