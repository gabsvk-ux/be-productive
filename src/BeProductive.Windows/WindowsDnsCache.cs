using System.Diagnostics;
using System.Runtime.Versioning;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDnsCache : IDnsCache
{
    public void Flush()
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Couldn't start ipconfig.");
        process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || process.ExitCode != 0)
            throw new InvalidOperationException("ipconfig /flushdns failed.");
    }
}
