using BeProductive.Core.Sessions;

namespace BeProductive.Service;

/// <summary>Runs the session clock once a second.</summary>
public sealed class SessionWorker(SessionService sessions, TimeProvider time, ILogger<SessionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        do
        {
            try
            {
                await sessions.TickAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Session tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
