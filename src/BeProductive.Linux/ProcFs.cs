namespace BeProductive.Linux;

/// <param name="Name">Normalized program name: the lowercase file name of the executable.</param>
/// <param name="ExecutablePath">The full path of the executable, as the kernel reports it.</param>
/// <param name="CommandLine">The arguments, joined by spaces.</param>
public sealed record LinuxProcess(int Pid, int Uid, string Name, string ExecutablePath, string CommandLine);

/// <summary>Reads the process table from <c>/proc</c>.</summary>
public sealed class ProcFs(LinuxRoot root)
{
    public IReadOnlyList<LinuxProcess> Processes()
    {
        var proc = root.Resolve("/proc");
        if (!Directory.Exists(proc))
            return [];

        var result = new List<LinuxProcess>();
        foreach (var directory in Directory.EnumerateDirectories(proc))
        {
            if (int.TryParse(Path.GetFileName(directory), out var pid) && Read(pid) is { } process)
                result.Add(process);
        }

        return result;
    }

    /// <summary>The process, or null if it's gone or is a kernel thread (which has no executable).</summary>
    public LinuxProcess? Read(int pid)
    {
        var directory = root.Resolve($"/proc/{pid}");
        try
        {
            var uid = RealUid(File.ReadAllLines(Path.Combine(directory, "status")));
            if (uid is null)
                return null;

            var executable = ExecutablePath(Path.Combine(directory, "exe"));
            if (executable is null)
                return null;

            var commandLine = File.ReadAllText(Path.Combine(directory, "cmdline")).Replace('\0', ' ').Trim();
            return new LinuxProcess(pid, uid.Value, NameOf(executable), executable, commandLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // It ended, or we may not look at it: skip it rather than guess.
            return null;
        }
    }

    /// <summary>The executable's file name, lowercased, without the " (deleted)" the kernel adds after an update.</summary>
    internal static string NameOf(string executablePath)
    {
        var path = executablePath.EndsWith(" (deleted)", StringComparison.Ordinal) ? executablePath[..^10] : executablePath;
        return Path.GetFileName(path).ToLowerInvariant();
    }

    /// <summary>The real user id from the "Uid:" line of <c>/proc/&lt;pid&gt;/status</c>.</summary>
    internal static int? RealUid(IEnumerable<string> statusLines)
    {
        foreach (var line in statusLines)
        {
            if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                continue;
            var fields = line[4..].Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            return fields.Length > 0 && int.TryParse(fields[0], out var uid) ? uid : null;
        }

        return null;
    }

    private static string? ExecutablePath(string exeLink)
    {
        // /proc/<pid>/exe is a symlink on a real system; the fake one in tests is a plain file holding the path.
        var info = new FileInfo(exeLink);
        if (info.LinkTarget is { } target)
            return target;
        if (info.Exists)
            return File.ReadAllText(exeLink).Trim() is { Length: > 0 } text ? text : null;
        return null;
    }
}
