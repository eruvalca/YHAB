namespace YHAB.Features.Budgeting.Services;

internal sealed class RecurringWorker(IServiceScopeFactory scopes, TimeProvider timeProvider,
    IConfiguration configuration, ILogger<RecurringWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> _postingFailed = LoggerMessage.Define(LogLevel.Error,
        new EventId(4100, "RecurringPostingFailed"), "Recurring posting failed; the next batch will retry.");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Budgeting:AutomaticPosting", true))
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), timeProvider);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<RecurringProcessor>().RunBatchAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Preserve failure details and retry on the next tick. Ledger and cursor changes are atomic.
                    _postingFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
