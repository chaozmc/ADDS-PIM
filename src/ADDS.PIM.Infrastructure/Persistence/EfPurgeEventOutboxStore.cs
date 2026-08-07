using System.Security.Cryptography;
using ADDS.PIM.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfPurgeEventOutboxStore(PimDbContext dbContext) : IPurgeEventOutboxStore
{
    public async Task<IReadOnlyList<PurgeEventOutboxMessage>> ListPendingAsync(int maximumCount, CancellationToken cancellationToken)
    {
        if (maximumCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var entries = await dbContext.PurgeEventOutbox.AsNoTracking()
            .Where(entry => entry.DeliveredUtc == null)
            .OrderBy(entry => entry.CreatedUtc).ThenBy(entry => entry.OutboxId)
            .Take(maximumCount)
            .ToArrayAsync(cancellationToken);
        return entries.Select(Map).ToArray();
    }

    public Task<bool> MarkDeliveredAsync(Guid outboxId, byte[] rowVersion, DateTimeOffset deliveredUtc, CancellationToken cancellationToken)
        => UpdateAsync(outboxId, rowVersion, entity =>
        {
            entity.DeliveryAttemptCount++;
            entity.LastAttemptUtc = deliveredUtc;
            entity.DeliveredUtc = deliveredUtc;
            entity.LastFailureCategory = null;
        }, cancellationToken);

    public Task<bool> RecordDeliveryFailureAsync(Guid outboxId, byte[] rowVersion, DateTimeOffset attemptedUtc, string failureCategory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(failureCategory) || failureCategory.Length > 64) throw new ArgumentException("A bounded failure category is required.", nameof(failureCategory));
        return UpdateAsync(outboxId, rowVersion, entity =>
        {
            entity.DeliveryAttemptCount++;
            entity.LastAttemptUtc = attemptedUtc;
            entity.LastFailureCategory = failureCategory;
        }, cancellationToken);
    }

    private async Task<bool> UpdateAsync(Guid outboxId, byte[] rowVersion, Action<Entities.PurgeEventOutboxEntity> update, CancellationToken cancellationToken)
    {
        if (outboxId == Guid.Empty || rowVersion.Length == 0) return false;
        var entity = await dbContext.PurgeEventOutbox.SingleOrDefaultAsync(entry => entry.OutboxId == outboxId, cancellationToken);
        if (entity is null || entity.DeliveredUtc is not null || !CryptographicOperations.FixedTimeEquals(entity.RowVersion, rowVersion)) return false;
        update(entity);
        try { await dbContext.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    private static PurgeEventOutboxMessage Map(Entities.PurgeEventOutboxEntity entity)
        => new(entity.OutboxId, entity.EventId, entity.EventType, entity.CorrelationId, entity.Payload, entity.CreatedUtc, entity.DeliveryAttemptCount, entity.LastAttemptUtc, entity.DeliveredUtc, entity.LastFailureCategory, entity.RowVersion);
}
