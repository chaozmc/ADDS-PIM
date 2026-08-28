using ADDS.PIM.Application.Notifications;
using ADDS.PIM.Application.Security;

namespace ADDS.PIM.Api;

public sealed class MailNotificationOutboxDispatcher(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<MailNotificationOutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IMailNotificationOutboxStore>();
            var mailSettingsProvider = scope.ServiceProvider.GetRequiredService<IResolvedMailSettingsProvider>();
            var mailSender = scope.ServiceProvider.GetRequiredService<IMailSender>();
            var pending = await store.ListPendingAsync(10, stoppingToken);
            if (pending.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                continue;
            }

            // One shared MailSettings row backs every pending message - resolve/decrypt it once per cycle
            // rather than once per message.
            var resolved = await mailSettingsProvider.GetAsync(stoppingToken);
            if (resolved.Status != ResolvedMailSettingsStatus.Resolved)
            {
                logger.LogWarning("Mail notification dispatch deferred for {Count} message(s): SMTP settings are not usable ({Status}).", pending.Count, resolved.Status);
                var deferredAt = timeProvider.GetUtcNow();
                foreach (var message in pending)
                {
                    try
                    {
                        await store.RecordDeliveryFailureAsync(message.OutboxId, message.RowVersion, deferredAt, $"MailSettings{resolved.Status}", stoppingToken);
                    }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(exception, "Failed to record mail notification delivery deferral for OutboxId {OutboxId}.", message.OutboxId);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            foreach (var message in pending)
            {
                var now = timeProvider.GetUtcNow();
                try
                {
                    var toAddresses = message.ToAddresses.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var ccAddresses = message.CcAddresses.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var bccAddresses = message.BccAddresses.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var outcome = await mailSender.SendAsync(new MailMessageSendRequest(resolved.Settings!.SmtpHost, resolved.Settings.SmtpPort, resolved.Settings.SenderAddress,
                        resolved.Settings.TlsMode, resolved.Settings.Username, resolved.Settings.Password, toAddresses, message.Subject, message.Body, ccAddresses, bccAddresses), stoppingToken);

                    if (outcome.Succeeded)
                    {
                        await store.MarkDeliveredAsync(message.OutboxId, message.RowVersion, now, stoppingToken);
                    }
                    else
                    {
                        logger.LogError("Mail notification delivery failed for OutboxId {OutboxId}: {ErrorMessage}", message.OutboxId, outcome.ErrorMessage);
                        await store.RecordDeliveryFailureAsync(message.OutboxId, message.RowVersion, now, outcome.ErrorMessage ?? "Send failed.", stoppingToken);
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "Mail notification dispatch threw for OutboxId {OutboxId}.", message.OutboxId);
                    try
                    {
                        await store.RecordDeliveryFailureAsync(message.OutboxId, message.RowVersion, now, "Unhandled exception during dispatch.", stoppingToken);
                    }
                    catch (Exception reportingException) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogCritical(reportingException, "Unable to record the mail notification delivery failure for OutboxId {OutboxId}.", message.OutboxId);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    break;
                }
            }
        }
    }
}
