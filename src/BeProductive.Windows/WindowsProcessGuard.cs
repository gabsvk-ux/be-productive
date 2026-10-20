using System.Diagnostics;
using System.Runtime.Versioning;
using BeProductive.Core.Enforcement;

namespace BeProductive.Windows;

/// <summary>
/// Lists and ends programs in the signed-in user's own session only. A hard-coded safe list of shell and
/// system programs is never ended, whatever a preset says, so a bad allowlist can't take down the desktop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessGuard : IProcessGuard
{
    // Normalized names (lowercase, no ".exe"). The shell, the compositor, the app and the service itself,
    // plus session-critical processes that happen to run in the user's session.
    private static readonly HashSet<string> NeverKill = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "sihost", "ctfmon", "startmenuexperiencehost", "searchhost",
        "shellexperiencehost", "applicationframehost", "textinputhost", "runtimebroker",
        "fontdrvhost", "csrss", "winlogon", "lsass", "services", "svchost", "wininit", "smss",
        "beproductive", "beproductive.service",
    };

    public IReadOnlyList<RunningProcess> UserProcesses()
    {
        var session = WindowsUserSession.ActiveSessionId();
        if (session < 0)
            return [];

        var result = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.SessionId != session)
                    continue;
                var name = process.ProcessName.ToLowerInvariant();
                if (NeverKill.Contains(name))
                    continue;
                result.Add(new RunningProcess(process.Id, name));
            }
            catch
            {
                // The process ended or we can't read it; skip it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    public bool Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.SessionId != WindowsUserSession.ActiveSessionId() || NeverKill.Contains(process.ProcessName.ToLowerInvariant()))
                return false;
            process.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
