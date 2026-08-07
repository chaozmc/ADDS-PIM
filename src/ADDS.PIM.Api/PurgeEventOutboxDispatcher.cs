using ADDS.PIM.Application.Audit;

namespace ADDS.PIM.Api;

public sealed class PurgeEventOutboxDispatcher(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<PurgeEventOutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IPurgeEventOutboxStore>();
            var eventLog = scope.ServiceProvider.GetRequiredService<IPurgeEventLog>();
            var pending = await store.ListPendingAsync(10, stoppingToken);
            if (pending.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                continue;
            }

            foreach (var message in pending)
            {
                var now = timeProvider.GetUtcNow();
                try
                {
                    await eventLog.WriteAsync(new(message.EventId, message.EventType, message.CorrelationId, $"outboxId={message.OutboxId:D};{message.Payload}"), stoppingToken);
                    await store.MarkDeliveredAsync(message.OutboxId, message.RowVersion, now, stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "Purge completion Event Log delivery failed for outbox {OutboxId}.", message.OutboxId);
                    try
                    {
                        await store.RecordDeliveryFailureAsync(message.OutboxId, message.RowVersion, now, "Configuration", stoppingToken);
                        await eventLog.WriteAsync(new(PurgeEventLogIds.CompletionDeliveryFailed, "PimIdentityPurgeCompletionDeliveryFailed", message.CorrelationId, $"outboxId={message.OutboxId:D};failureCategory=Configuration"), stoppingToken);
                    }
                    catch (Exception reportingException) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogCritical(reportingException, "Unable to record the purge completion Event Log delivery failure.");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    break;
                }
            }
        }
    }
}
