using BeProductive.Core.Enforcement;
using BeProductive.Linux;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BeProductive.Linux.Tests;

/// <summary>A throwaway Linux file system: fake /proc, logind sessions, .desktop files and /etc.</summary>
public sealed class FakeLinux : IDisposable
{
    public FakeLinux()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bp-linux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Root = new LinuxRoot(Path);
    }

    public string Path { get; }

    public LinuxRoot Root { get; }

    public FakeTimeProvider Time { get; } = new();

    public string Write(string absolute, string content)
    {
        var file = Root.Resolve(absolute);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    /// <summary>A logind session file, as logind writes it.</summary>
    public void Session(string id, int uid, string user, string type = "wayland", string @class = "user", bool active = true, string seat = "seat0") =>
        Write($"/run/systemd/sessions/{id}", $"""
            # This is private data. Do not parse.
            UID={uid}
            USER={user}
            ACTIVE={(active ? 1 : 0)}
            IS_DISPLAY=1
            STATE={(active ? "active" : "online")}
            REMOTE=0
            TYPE={type}
            CLASS={@class}
            SEAT={seat}
            LEADER=1000
            """);

    /// <summary>A process in the fake /proc. A null executable makes it look like a kernel thread.</summary>
    public void Process(int pid, int uid, string? executable, string commandLine = "")
    {
        Write($"/proc/{pid}/status", $"Name:\tx\nUmask:\t0022\nState:\tS (sleeping)\nUid:\t{uid}\t{uid}\t{uid}\t{uid}\nGid:\t{uid}\t{uid}\t{uid}\t{uid}\n");
        Write($"/proc/{pid}/cmdline", commandLine.Replace(' ', '\0'));
        if (executable is not null)
            Write($"/proc/{pid}/exe", executable);
    }

    public void DesktopEntry(string name, string body, string directory = "/usr/share/applications") =>
        Write($"{directory}/{name}.desktop", "[Desktop Entry]\nType=Application\nName=" + name + "\n" + body + "\n");

    public LinuxProcessGuard Guard(EnforcementOptions? options = null) =>
        new(new ProcFs(Root), new LinuxUserSession(Root), new DesktopApps(Root, Time),
            Options.Create(options ?? new EnforcementOptions { TerminalApps = ["gnome-terminal-server"], AdminToolApps = [], BrowserApp = "chrome" }));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
