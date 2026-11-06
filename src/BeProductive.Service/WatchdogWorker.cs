using BeProductive.Core.Enforcement;
using BeProductive.Core.Sessions;

namespace BeProductive.Service;

/// <summary>Checks a few times a minute that the gate is still up while the laptop is locked.</summary>
public sealed class WatchdogWorker(SessionService sessions, Watchdog watchdog, TimeProvider time, ILogger<WatchdogWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                var status = await sessions.SnapshotAsync(stoppingToken);
                watchdog.Tick(status, time.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Watchdog check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
