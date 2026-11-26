using System.Diagnostics;
using System.Runtime.Versioning;
using BeProductive.Core.Enforcement;

namespace BeProductive.Windows;

/// <summary>
/// Lists and ends programs in the signed-in user's own session only. Windows' own binaries (anything under
/// %WINDIR%) are never ended — that protects the shell and its helper processes (Explorer spins up dllhost,
/// search hosts and the like when you click around; killing those crashes Explorer) — except for a short
/// explicit set of terminals/admin tools that live there and still need policing. Real distraction apps live
/// outside %WINDIR% (Program Files, AppData…), so blocking them is unaffected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessGuard : IProcessGuard
{
    // Normalized names (lowercase, no ".exe"). The shell, the compositor, the app and the service itself,
    // plus session-critical processes. "dotnet" is here because in development the app and the service both
    // run as dotnet.exe — without it the enforcer would kill its own host and the app during a focus block.
    private static readonly HashSet<string> NeverKill = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "sihost", "ctfmon", "startmenuexperiencehost", "searchhost",
        "shellexperiencehost", "applicationframehost", "textinputhost", "runtimebroker",
        "fontdrvhost", "csrss", "winlogon", "lsass", "services", "svchost", "wininit", "smss",
        "logonui", "lockapp", "lockapphost", "consent", "credentialuihost", "credentialuibroker",
        "userinit", "atbroker", "useroobebroker",
        "beproductive", "beproductive.service", "dotnet",
    };

    // Programs we still end even though they live under %WINDIR%: the terminals and admin tools. (cmd, regedit,
    // taskmgr and the MMC are also blocked by policy; killing is a backstop. PowerShell is the one that policy
    // doesn't cover, so this is what actually keeps it blocked.)
    private static readonly HashSet<string> PoliceEvenInWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "cmd", "wscript", "cscript", "mshta", "regedit", "taskmgr", "mmc",
    };

    private static readonly string WindowsPrefix =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + '\\';

    // The service's own process and the host that launched it (the "dotnet run" wrapper in development),
    // so enforcement can never take itself down whatever it is named.
    private static readonly HashSet<int> ProtectedPids = CurrentProcessTree();

    public IReadOnlyList<RunningProcess> UserProcesses()
    {
        var session = WindowsUserSession.ActiveSessionId();
        if (session < 0)
            return [];

        // Never police processes at the logon screen (before sign-in): the only things there are the logon
        // UI, and killing those blacks out the login screen. While merely locked, a user is still signed in
        // and the lock-screen / shell UI is protected below.
        if (!WindowsUserSession.IsUserSignedIn())
            return [];

        var result = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.SessionId != session || ProtectedPids.Contains(process.Id))
                    continue;
                var name = process.ProcessName.ToLowerInvariant();
                if (IsProtected(process, name))
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
        if (ProtectedPids.Contains(pid) || !WindowsUserSession.IsUserSignedIn())
            return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.SessionId != WindowsUserSession.ActiveSessionId() || IsProtected(process, process.ProcessName.ToLowerInvariant()))
                return false;
            process.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A process is protected if it's on the never-kill list, or it's a Windows binary that isn't one
    /// of the terminals/admin tools we still police.</summary>
    private static bool IsProtected(Process process, string name)
    {
        if (NeverKill.Contains(name))
            return true;
        if (PoliceEvenInWindows.Contains(name))
            return false;
        return IsWindowsBinary(process);
    }

    /// <summary>Whether the process's executable lives under %WINDIR%. Protects on failure (can't read => leave it).</summary>
    private static bool IsWindowsBinary(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return path is null || path.StartsWith(WindowsPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static HashSet<int> CurrentProcessTree()
    {
        var pids = new HashSet<int> { Environment.ProcessId };
        try
        {
            foreach (var process in Process.GetProcesses())
            {
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
