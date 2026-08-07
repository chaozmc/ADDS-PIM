using System.Security.Cryptography.X509Certificates;
using ADDS.PIM.Application.Worker;

namespace ADDS.PIM.Infrastructure.Worker;

/// <summary>
/// Thread-safe handoff between <see cref="HttpsWorkerMembershipClient.CreateHandler"/>'s synchronous, I/O-free
/// TLS validation callback and <see cref="HttpsWorkerMembershipClient"/>'s async persistence of the observed
/// Worker server certificate. The callback only records here; the client reads and clears it after the HTTP
/// call completes, on its own time, so the TLS handshake itself never waits on a database write.
/// </summary>
public sealed class WorkerServerCertificateObservationCache
{
    private readonly object gate = new();
    private WorkerServerCertificateObservation? current;

    public void Record(X509Certificate2 certificate, bool wasAccepted, DateTimeOffset observedUtc)
    {
        var observation = new WorkerServerCertificateObservation(
            WorkerClientCertificateProvider.NormalizeThumbprint(certificate.Thumbprint),
            certificate.NotBefore,
            certificate.NotAfter,
            wasAccepted,
            observedUtc);
        lock (gate) { current = observation; }
    }

    public WorkerServerCertificateObservation? TakeLatest()
    {
        lock (gate)
        {
            var value = current;
            current = null;
            return value;
        }
    }
}
