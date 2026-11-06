using BeProductive.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.Enforcement;

public sealed class WatchdogOptions
{
    public const string SectionName = "Watchdog";

    public bool Enabled { get; set; } = true;

    /// <summary>Wait at least this long between relaunch attempts, so a crash-looping app isn't hammered.</summary>
    public TimeSpan RelaunchBackoff { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Keeps the gate on screen while the laptop is locked: if the app is killed, it is started again, and the
/// kill is recorded as a tamper attempt (so it shows in the stats). It only acts when enforcement is live;
/// in dry run or panic it stands back, so those modes stay true no-ops. Call <see cref="Tick"/> every few seconds.
/// </summary>
public sealed class Watchdog(
    IAppLauncher launcher,
    EnforcementGate gate,
    ISessionStore store,
    IOptions<WatchdogOptions> options,
    ILogger<Watchdog> logger)
{
    private readonly object sync = new();
    private bool seenRunning;
    private DateTimeOffset lastRelaunch = DateTimeOffset.MinValue;
    private DateTimeOffset lastTamperLogged = DateTimeOffset.MinValue;

    public void Tick(SessionStatus status, DateTimeOffset now)
    {
        if (!options.Value.Enabled)
            return;

        lock (sync)
        {
            // The gate must be visible whenever the day is locked. When nothing is locked the app is just a
            // tray icon the user may close freely, so don't police it.
            if (!LockRequiresApp(status))
            {
                seenRunning = false;
                return;
            }

            if (launcher.IsRunning())
            {
                seenRunning = true;
                return;
            }

            // The app disappeared while it should be up. If we'd seen it running, that's a deliberate kill.
            if (seenRunning)
            {
                RecordTamper(now);
                seenRunning = false;
            }

            if (now - lastRelaunch < options.Value.RelaunchBackoff)
                return;
            lastRelaunch = now;

            if (gate.Mode != EnforcementMode.Live)
            {
                logger.LogInformation("[{Mode}] Would relaunch the app", gate.Mode);
                return;
            }

            if (launcher.TryLaunch())
                logger.LogWarning("Relaunched the app after it was closed");
            else
                logger.LogError("The app was closed and couldn't be relaunched");
        }
    }

    private void RecordTamper(DateTimeOffset now)
    {
        // Throttle, so one kill (seen on the next tick) is one event, not a stream.
        if (now - lastTamperLogged < TimeSpan.FromSeconds(30))
            return;
        lastTamperLogged = now;
        logger.LogWarning("The app was closed while the laptop was locked (recorded as a tamper attempt)");
        store.RecordEvent(new SessionEvent(now, SessionEventKinds.TamperAttempt, "app-closed"));
    }

    private static bool LockRequiresApp(SessionStatus status) =>
        status.Restriction != Restriction.None || status.AdminToolsBlocked;
}
