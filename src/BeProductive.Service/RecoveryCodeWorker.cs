using BeProductive.Core.Recovery;

namespace BeProductive.Service;

/// <summary>When every recovery code is used up, keeps trying to send your friend a new set until it works.</summary>
public sealed class RecoveryCodeWorker(RecoveryCodeService codes, TimeProvider time, ILogger<RecoveryCodeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RetryInterval, time);
        do
        {
            try
            {
                if (codes.GetStatus() is { HasCodes: true, Remaining: 0 })
                    await codes.RegenerateAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "All recovery codes are used and a new set couldn't be sent to your friend. Retrying in {Interval}", RetryInterval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
