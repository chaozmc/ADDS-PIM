namespace ADDS.PIM.Application.Worker;

/// <summary>
/// Best-effort diagnostic cache of the Worker's TLS server certificate as last observed during an actual
/// API-to-Worker mTLS handshake, piggybacked onto real membership dispatches - never from a
/// dedicated probe. Read by the admin certificate overview; written by <c>HttpsWorkerMembershipClient</c>,
/// which must treat every failure here as non-fatal to the real dispatch outcome.
/// </summary>
public interface IWorkerServerCertificateObservationStore
{
    Task RecordAsync(WorkerServerCertificateObservation observation, CancellationToken cancellationToken);

    Task<WorkerServerCertificateObservation?> GetLatestAsync(CancellationToken cancellationToken);
}

public sealed record WorkerServerCertificateObservation(
    string Thumbprint,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    bool WasAccepted,
    DateTimeOffset ObservedUtc);
