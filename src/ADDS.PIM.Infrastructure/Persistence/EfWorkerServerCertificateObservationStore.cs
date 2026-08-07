using ADDS.PIM.Application.Worker;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfWorkerServerCertificateObservationStore(PimDbContext dbContext) : IWorkerServerCertificateObservationStore
{
    private const int SingletonObservationId = 1;

    public async Task RecordAsync(WorkerServerCertificateObservation observation, CancellationToken cancellationToken)
    {
        var entity = await dbContext.WorkerServerCertificateObservations.SingleOrDefaultAsync(x => x.ObservationId == SingletonObservationId, cancellationToken);
        if (entity is null)
        {
            entity = new WorkerServerCertificateObservationEntity { ObservationId = SingletonObservationId, Thumbprint = observation.Thumbprint };
            dbContext.WorkerServerCertificateObservations.Add(entity);
        }

        entity.Thumbprint = observation.Thumbprint;
        entity.NotBeforeUtc = observation.NotBeforeUtc;
        entity.NotAfterUtc = observation.NotAfterUtc;
        entity.WasAccepted = observation.WasAccepted;
        entity.ObservedUtc = observation.ObservedUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkerServerCertificateObservation?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var entity = await dbContext.WorkerServerCertificateObservations.AsNoTracking().SingleOrDefaultAsync(x => x.ObservationId == SingletonObservationId, cancellationToken);
        return entity is null ? null : new WorkerServerCertificateObservation(entity.Thumbprint, entity.NotBeforeUtc, entity.NotAfterUtc, entity.WasAccepted, entity.ObservedUtc);
    }
}
