using System.Diagnostics;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Linux;

/// <summary>
/// Flushes whichever system DNS cache is running: systemd-resolved (most desktops) or nscd. Many systems run
/// neither, and then there is nothing to flush. Browsers keep their own cache, as on Windows.
/// </summary>
public sealed class LinuxDnsCache(LinuxRoot root) : IDnsCache
{
    public void Flush()
    {
        if (Directory.Exists(root.Resolve("/run/systemd/resolve")) && Run("resolvectl", "flush-caches"))
            return;
        if (File.Exists(root.Resolve("/run/nscd/socket")))
            Run("nscd", "--invalidate=hosts");
    }

    private static bool Run(string program, string argument)
    {
        var start = new ProcessStartInfo(program, argument)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return false;
            process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || process.ExitCode != 0)
                throw new InvalidOperationException($"{program} {argument} failed.");
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The program isn't installed.
            return false;
        }
    }
}
