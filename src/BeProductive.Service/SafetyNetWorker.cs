using BeProductive.Core.Enforcement;
using BeProductive.Core.SafetyNet;
using Microsoft.Extensions.Options;

namespace BeProductive.Service;

/// <summary>
/// Makes sure nothing stays applied that shouldn't: leftovers from a previous run are undone at start,
/// expired leases and the panic file are checked regularly, and everything is undone when the service stops.
/// </summary>
public sealed class SafetyNetWorker(
    SystemChanges changes,
    EnforcementGate gate,
    IOptions<SafetyNetOptions> options,
    TimeProvider time,
    ILogger<SafetyNetWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Enforcement mode: {Mode}", gate.Mode);
        changes.RestoreAll("they were left over from a previous run");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CheckInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (gate.Mode == EnforcementMode.Panic)
                    changes.RestoreAll("the panic file is present");
                else
                    changes.RestoreIfExpired();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Safety net check failed");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        changes.RestoreAll("the service is stopping");
    }
}
