namespace ADDS.PIM.Infrastructure.Persistence.Entities;

/// <summary>
/// Single-row, upserted cache of the Worker's TLS server certificate as last observed during an actual
/// API-to-Worker mTLS handshake - never written from a dedicated probe, only piggybacked onto real command
/// dispatches (see <c>HttpsWorkerMembershipClient</c>). <see cref="ObservationId"/> is always 1.
/// </summary>
public sealed class WorkerServerCertificateObservationEntity
{
    public int ObservationId { get; set; }
    public required string Thumbprint { get; set; }
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset NotAfterUtc { get; set; }
    public bool WasAccepted { get; set; }
    public DateTimeOffset ObservedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
