using ADDS.PIM.Contracts.Worker.V1;

namespace ADDS.PIM.Application.Worker;

public enum WorkerCommandStatus
{
    Received = 1,
    Executing = 2,
    VerificationPending = 3,
    Succeeded = 4,
    Rejected = 5,
    Failed = 6
}

public enum WorkerCommandRegistrationKind
{
    Accepted,
    Existing,
    ReplayConflict
}

public sealed record WorkerCommandRegistration(
    WorkerCommandRegistrationKind Kind,
    WorkerCommandStatus? ExistingStatus,
    TemporaryGroupMembershipResult? ExistingResult);

public interface IWorkerCommandStore
{
    Task<WorkerCommandRegistration> RegisterAsync(
        TemporaryGroupMembershipCommand command,
        string callerCertificateThumbprint,
        DateTimeOffset receivedUtc,
        CancellationToken cancellationToken);

    Task SetStatusAsync(Guid commandId, WorkerCommandStatus status, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the terminal result before it is returned to the API. An
    /// identical retry can then observe the durable outcome without causing a
    /// second AD operation.
    /// </summary>
    Task CompleteAsync(
        Guid commandId,
        WorkerCommandStatus status,
        TemporaryGroupMembershipResult result,
        CancellationToken cancellationToken);
}
