using System.Diagnostics;
using BeProductive.Core.Enforcement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BeProductive.Linux;

/// <summary>
/// Checks for and (re)launches the desktop app for the user signed in at the screen. The service runs as root
/// with no display, so it asks the user's own systemd manager to start the app (<c>systemd-run --user
/// --machine=user@</c>); the desktop session has already given that manager its DISPLAY / WAYLAND_DISPLAY.
/// </summary>
public sealed class LinuxAppLauncher(
    ProcFs proc,
    LinuxUserSession sessions,
    IConfiguration configuration,
    ILogger<LinuxAppLauncher> logger) : IAppLauncher
{
    private readonly string appPath = ResolveAppPath(configuration);

    public bool IsUserSignedIn() => sessions.ActiveGraphicalSession() is not null;

    public bool IsRunning()
    {
        if (sessions.ActiveGraphicalSession() is not { } session)
            return false;
        return proc.Processes().Any(process => process.Uid == session.Uid && IsApp(process));
    }

    public bool TryLaunch()
    {
        if (sessions.ActiveGraphicalSession() is not { } session || session.User.Length == 0)
            return false;
        if (appPath.Length == 0 || !File.Exists(appPath))
        {
            logger.LogError("Can't relaunch the app: the executable wasn't found at '{Path}'. Set Watchdog:AppPath.", appPath);
            return false;
        }

        var start = new ProcessStartInfo("systemd-run")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in (string[])["--user", $"--machine={session.User}@", "--collect", "--quiet", "--", appPath])
            start.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return false;
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || process.ExitCode != 0)
            {
                logger.LogError("Can't relaunch the app: systemd-run failed: {Error}", error.IsCompleted ? error.Result.Trim() : "timed out");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Can't relaunch the app: systemd-run couldn't be started");
            return false;
        }
    }

    /// <summary>The published app (<c>BeProductive</c>), or a development run (<c>dotnet BeProductive.dll</c>).</summary>
    internal static bool IsApp(LinuxProcess process) =>
        process.Name == "beproductive" ||
        (process.Name == "dotnet" && process.CommandLine.Contains("BeProductive.dll", StringComparison.Ordinal));

    /// <summary>Config <c>Watchdog:AppPath</c>, else the app installed next to the service at <c>../App/BeProductive</c>.</summary>
    private static string ResolveAppPath(IConfiguration configuration) =>
        configuration["Watchdog:AppPath"] is { Length: > 0 } configured
            ? configured
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "App", "BeProductive"));
}
