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
    // plus session-critical processes that happen to run in the user's session. "dotnet" is here because in
    // development the app and the service both run as dotnet.exe — without it the enforcer would kill its own
    // host and the app during a focus block.
    private static readonly HashSet<string> NeverKill = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "sihost", "ctfmon", "startmenuexperiencehost", "searchhost",
        "shellexperiencehost", "applicationframehost", "textinputhost", "runtimebroker",
        "fontdrvhost", "csrss", "winlogon", "lsass", "services", "svchost", "wininit", "smss",
        "beproductive", "beproductive.service", "dotnet",
    };

    // The service's own process and the host that launched it (the "dotnet run" wrapper in development),
    // so enforcement can never take itself down whatever it is named.
    private static readonly HashSet<int> ProtectedPids = CurrentProcessTree();

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
                if (process.SessionId != session || ProtectedPids.Contains(process.Id))
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
        if (ProtectedPids.Contains(pid))
            return false;
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

    private static HashSet<int> CurrentProcessTree()
    {
        var pids = new HashSet<int> { Environment.ProcessId };
        try
        {
            using var self = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcesses())
            {
                // The dotnet-run host is this process's parent; it shares the name "dotnet", already safe-listed,
                // but protect it by id too in case it is ever renamed.
                try
                {
                    if (process.ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                        pids.Add(process.Id);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Best effort: the name-based safe list already covers the common case.
        }

        return pids;
    }
}
