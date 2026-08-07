using ADDS.PIM.Application.Administration;

public sealed class DirectoryReconciliationHostedService(IServiceScopeFactory scopeFactory, ILogger<DirectoryReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var reconciliation = scope.ServiceProvider.GetRequiredService<DirectoryReconciliationUseCase>();
                var processed = await reconciliation.ExecuteNextAsync(stoppingToken);
                await Task.Delay(processed ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Directory reconciliation maintenance worker failed before completing a run.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
