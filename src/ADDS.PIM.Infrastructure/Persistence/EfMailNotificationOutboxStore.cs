using System.Security.Cryptography;
using ADDS.PIM.Application.Notifications;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfMailNotificationOutboxStore(PimDbContext dbContext, ILogger<EfMailNotificationOutboxStore> logger) : IMailNotificationOutboxStore
{
    public async Task<IReadOnlyList<MailNotificationOutboxMessage>> ListPendingAsync(int maximumCount, CancellationToken cancellationToken)
    {
        if (maximumCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var entries = await dbContext.MailNotificationOutbox.AsNoTracking()
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
            entity.LastFailureMessage = null;
        }, cancellationToken);

    public Task<bool> RecordDeliveryFailureAsync(Guid outboxId, byte[] rowVersion, DateTimeOffset attemptedUtc, string failureMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(failureMessage) || failureMessage.Length > 512) throw new ArgumentException("A bounded failure message is required.", nameof(failureMessage));
        return UpdateAsync(outboxId, rowVersion, entity =>
        {
            entity.DeliveryAttemptCount++;
            entity.LastAttemptUtc = attemptedUtc;
            entity.LastFailureMessage = failureMessage;
        }, cancellationToken);
    }

    private async Task<bool> UpdateAsync(Guid outboxId, byte[] rowVersion, Action<MailNotificationOutboxEntity> update, CancellationToken cancellationToken)
    {
        if (outboxId == Guid.Empty || rowVersion.Length == 0) return false;
        var entity = await dbContext.MailNotificationOutbox.SingleOrDefaultAsync(entry => entry.OutboxId == outboxId, cancellationToken);
        if (entity is null || entity.DeliveredUtc is not null || !CryptographicOperations.FixedTimeEquals(entity.RowVersion, rowVersion)) return false;
        update(entity);
        try { await dbContext.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "UpdateAsync hit a concurrent update for OutboxId {OutboxId}.", outboxId); return false; }
    }

    private static MailNotificationOutboxMessage Map(MailNotificationOutboxEntity entity)
        => new(entity.OutboxId, entity.RequestId, entity.ToAddresses, entity.CcAddresses, entity.BccAddresses, entity.Subject, entity.Body, entity.CreatedUtc, entity.DeliveryAttemptCount, entity.LastAttemptUtc, entity.DeliveredUtc, entity.LastFailureMessage, entity.RowVersion);
}
