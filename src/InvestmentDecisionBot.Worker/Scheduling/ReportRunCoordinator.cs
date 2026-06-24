namespace InvestmentDecisionBot.Worker.Scheduling;

public sealed class ReportRunCoordinator
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<bool> TryRunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            await action(cancellationToken);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
