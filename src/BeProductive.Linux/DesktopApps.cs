namespace BeProductive.Linux;

/// <summary>
/// The program names of the apps a person can launch from the desktop, read from the installed
/// <c>.desktop</c> files. On Windows the allowlist can be checked against every process in the user's session,
/// but a Linux desktop session also runs dozens of user-owned helpers (the compositor, portals, sound, keyring,
/// indexers…) that must never be killed. So during focus only processes that are recognisably *apps* are
/// policed: anything that isn't one is left alone. That errs toward letting something run rather than
/// breaking the desktop.
/// </summary>
public sealed class DesktopApps(LinuxRoot root, TimeProvider time)
{
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(1);

    // Launchers that are interpreters or wrappers: their first word says nothing about the process name.
    private static readonly HashSet<string> Wrappers = new(StringComparer.OrdinalIgnoreCase)
    {
        "sh", "bash", "dash", "zsh", "env", "python", "python3", "perl", "ruby", "node", "java", "mono", "dotnet",
        "flatpak", "snap", "gtk-launch", "xdg-open", "gio", "pkexec", "sudo", "wine", "exec", "nice", "ionice",
        "systemd-run", "dbus-launch", "gapplication",
    };

    // Launcher name → the process it actually runs, where those differ.
    private static readonly Dictionary<string, string[]> KnownAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google-chrome"] = ["chrome"],
        ["google-chrome-stable"] = ["chrome"],
        ["google-chrome-beta"] = ["chrome"],
        ["google-chrome-unstable"] = ["chrome"],
        ["chromium-browser"] = ["chromium", "chrome"],
        ["microsoft-edge"] = ["msedge"],
        ["microsoft-edge-stable"] = ["msedge"],
        ["brave-browser"] = ["brave"],
        ["brave-browser-stable"] = ["brave"],
        ["opera"] = ["opera"],
        ["vivaldi-stable"] = ["vivaldi-bin"],
        ["firefox-esr"] = ["firefox-esr", "firefox"],
        ["thunderbird"] = ["thunderbird", "thunderbird-bin"],
        ["gnome-terminal"] = ["gnome-terminal-server"],
        ["steam"] = ["steam", "steamwebhelper"],
        ["discord"] = ["discord"],
        ["code"] = ["code"],
    };

    private readonly object sync = new();
    private HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset scannedAt = DateTimeOffset.MinValue;

    /// <summary>The app process names (lowercase). Rescanned at most once a minute.</summary>
    public IReadOnlySet<string> Names()
    {
        lock (sync)
        {
            var now = time.GetUtcNow();
            if (now - scannedAt >= RescanInterval)
            {
                names = Scan();
                scannedAt = now;
            }

            return names;
        }
    }

    private HashSet<string> Scan()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directories())
        {
            if (!Directory.Exists(directory))
                continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.desktop", SearchOption.AllDirectories).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    foreach (var name in NamesFrom(File.ReadLines(file)))
                        result.Add(name);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return result;
    }

    private IEnumerable<string> Directories()
    {
        yield return root.Resolve("/usr/share/applications");
        yield return root.Resolve("/usr/local/share/applications");
        yield return root.Resolve("/var/lib/flatpak/exports/share/applications");
        yield return root.Resolve("/var/lib/snapd/desktop/applications");

        // Every user's own launchers: ~/.local/share/applications (apps installed per user, AppImages that
        // were integrated, and so on).
        var home = root.Resolve("/home");
        if (Directory.Exists(home))
        {
            foreach (var user in SafeDirectories(home))
            {
                yield return System.IO.Path.Combine(user, ".local", "share", "applications");
                yield return System.IO.Path.Combine(user, ".local", "share", "flatpak", "exports", "share", "applications");
            }
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The process names one .desktop file stands for: the program in <c>Exec</c> (and <c>TryExec</c>), its known
    /// aliases, and <c>StartupWMClass</c>. Entries that are hidden, not applications, or launched through an
    /// interpreter or a sandbox runner (whose process name can't be known) give nothing.
    /// </summary>
    internal static IEnumerable<string> NamesFrom(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var inMainGroup = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inMainGroup = line == "[Desktop Entry]";
                continue;
            }

            if (!inMainGroup || line.StartsWith('#'))
                continue;
            var separator = line.IndexOf('=');
            if (separator > 0)
                values.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim());
        }

        if (values.GetValueOrDefault("Type", "Application") != "Application" ||
            IsTrue(values.GetValueOrDefault("Hidden")) || IsTrue(values.GetValueOrDefault("NoDisplay")))
            return [];

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in (string[])["Exec", "TryExec"])
        {
            if (values.TryGetValue(key, out var command) && ProgramOf(command) is { } program)
            {
                result.Add(program);
                if (KnownAliases.TryGetValue(program, out var aliases))
                    result.UnionWith(aliases);
            }
        }

        // Without a usable Exec the WM class alone isn't trusted; with one it catches renamed binaries.
        if (result.Count > 0 && values.TryGetValue("StartupWMClass", out var windowClass) && windowClass.Length > 0)
            result.Add(windowClass.ToLowerInvariant());
        return result;
    }

    /// <summary>The program an Exec line runs, as a lowercase file name, or null for wrappers and interpreters.</summary>
    internal static string? ProgramOf(string exec)
    {
        foreach (var word in Words(exec))
        {
            // "env FOO=bar app": skip the assignments and look at the program after them.
            if (IsAssignment(word))
                continue;
            var name = System.IO.Path.GetFileName(word).ToLowerInvariant();
            if (name.Length == 0 || name.StartsWith('%'))
                return null;
            if (name == "env")
                continue;
            return Wrappers.Contains(name) ? null : name;
        }

        return null;
    }

    private static IEnumerable<string> Words(string command)
    {
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in command)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    /// <summary>A "NAME=value" environment assignment, as in "env NAME=value app".</summary>
    private static bool IsAssignment(string word)
    {
        var separator = word.IndexOf('=');
        return separator > 0 && word[..separator].All(character => char.IsAsciiLetterOrDigit(character) || character == '_') && !char.IsAsciiDigit(word[0]);
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
