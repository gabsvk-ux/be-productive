using System.Diagnostics;
using BeProductive.Core.Enforcement;

namespace BeProductive.Linux.Tests;

public sealed class LinuxProcessGuardTests : IDisposable
{
    private readonly FakeLinux linux = new();

    public LinuxProcessGuardTests()
    {
        linux.DesktopEntry("firefox", "Exec=firefox %u");
        linux.DesktopEntry("discord", "Exec=/usr/share/discord/Discord");
        linux.DesktopEntry("nautilus", "Exec=nautilus --new-window");
    }

    public void Dispose() => linux.Dispose();

    private string[] Listed(EnforcementOptions? options = null) =>
        linux.Guard(options).UserProcesses().Select(process => process.Name).Order().ToArray();

    [Fact]
    public void Lists_only_the_signed_in_users_apps()
    {
        linux.Session("2", 1000, "alice");
        linux.Process(10, 1000, "/usr/lib/firefox/firefox");
        linux.Process(11, 1000, "/usr/share/discord/Discord");
        linux.Process(12, 1001, "/usr/lib/firefox/firefox");   // someone else's
        linux.Process(13, 0, "/usr/sbin/sshd");                 // the system's
        linux.Process(14, 1000, "/usr/libexec/gsd-power");      // a desktop helper, no launcher
        linux.Process(15, 1000, "/usr/bin/pipewire");

        Assert.Equal(["discord", "firefox"], Listed());
    }

    [Fact]
    public void The_terminals_admin_tools_and_browser_are_always_candidates()
    {
        linux.Session("2", 1000, "alice");
        linux.Process(10, 1000, "/usr/libexec/gnome-terminal-server");
        linux.Process(11, 1000, "/opt/google/chrome/chrome");
        linux.Process(12, 1000, "/usr/bin/gnome-system-monitor");

        var options = new EnforcementOptions { TerminalApps = ["gnome-terminal-server"], AdminToolApps = ["gnome-system-monitor"], BrowserApp = "chrome" };
        Assert.Equal(["chrome", "gnome-system-monitor", "gnome-terminal-server"], Listed(options));
    }

    [Fact]
    public void The_shell_file_manager_and_lock_screen_are_never_listed_even_with_a_launcher()
    {
        linux.Session("2", 1000, "alice");
        linux.DesktopEntry("shell", "Exec=gnome-shell");
        linux.Process(10, 1000, "/usr/bin/gnome-shell");
        linux.Process(11, 1000, "/usr/bin/nautilus");
        linux.Process(12, 1000, "/usr/bin/swaylock");

        Assert.Empty(Listed(new EnforcementOptions { TerminalApps = ["swaylock"], AdminToolApps = [] }));
    }

    [Theory]
    [InlineData("gsd-media-keys")]
    [InlineData("xdg-desktop-portal-gnome")]
    [InlineData("kscreenlocker_greet")]
    [InlineData("beproductive")]
    [InlineData("dotnet")]
    public void Session_programs_are_protected(string name)
    {
        Assert.True(LinuxProcessGuard.IsProtected(name));
    }

    [Fact]
    public void Nothing_is_policed_at_the_login_screen()
    {
        linux.Session("c1", 120, "gdm", @class: "greeter");
        linux.Process(10, 120, "/usr/lib/firefox/firefox");

        Assert.Empty(Listed());
    }

    [Fact]
    public void Nothing_is_policed_without_a_graphical_session()
    {
        linux.Session("3", 1000, "alice", type: "tty");
        linux.Process(10, 1000, "/usr/lib/firefox/firefox");

        Assert.Empty(Listed());
        Assert.False(linux.Guard().Kill(10));
    }

    [Fact]
    public void Kill_rechecks_the_process_and_refuses_protected_or_unknown_ones()
    {
        linux.Session("2", 1000, "alice");
        linux.Process(11, 1000, "/usr/bin/nautilus");

        var guard = linux.Guard();
        Assert.False(guard.Kill(11));
        Assert.False(guard.Kill(999_999));
    }

    [Fact]
    public void Kill_ends_a_real_process_that_is_listed()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var sleeper = Process.Start(new ProcessStartInfo("sleep", "60") { UseShellExecute = false })!;
        linux.Session("2", 1000, "alice");
        linux.Process(sleeper.Id, 1000, "/usr/bin/sleep");
        var guard = linux.Guard(new EnforcementOptions { TerminalApps = ["sleep"], AdminToolApps = [] });

        Assert.Contains(guard.UserProcesses(), process => process.Pid == sleeper.Id);
        Assert.True(guard.Kill(sleeper.Id));
        Assert.True(sleeper.WaitForExit(5_000));
    }
}
