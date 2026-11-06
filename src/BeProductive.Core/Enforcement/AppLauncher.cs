namespace BeProductive.Core.Enforcement;

/// <summary>
/// Starts and checks the per-user app from the service. A Windows service runs with no desktop, so it
/// can't draw the gate itself — it (re)launches the app process in the signed-in user's session.
/// </summary>
public interface IAppLauncher
{
    /// <summary>Is the app running in the signed-in user's session right now?</summary>
    bool IsRunning();

    /// <summary>Starts the app in the signed-in user's session. Returns false if it couldn't be started.</summary>
    bool TryLaunch();
}

/// <summary>Does nothing: for systems without an implementation yet, so the watchdog still runs and logs.</summary>
public sealed class NullAppLauncher : IAppLauncher
{
    public bool IsRunning() => false;

    public bool TryLaunch() => false;
}
