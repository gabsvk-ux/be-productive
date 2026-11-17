using BeProductive.Core.Enforcement;
using Microsoft.Extensions.Options;

namespace BeProductive.Linux;

/// <summary>
/// Lists and ends programs belonging to the user signed in at the screen. Three layers keep the desktop safe:
/// nothing is policed unless a graphical user session is in front (never at the login screen); only
/// processes that are recognisably apps (<see cref="DesktopApps"/>, plus the configured terminal, admin-tool
/// and browser names) are ever candidates; and a hard-coded never-kill list of shell, session and lock-screen
/// programs is skipped whatever a preset says.
/// </summary>
public sealed class LinuxProcessGuard(
    ProcFs proc,
    LinuxUserSession sessions,
    DesktopApps desktopApps,
    IOptions<EnforcementOptions> options) : IProcessGuard
{
    // Lowercase executable names. The desktop shells and compositors, session managers, file managers (the
    // Linux counterpart of explorer, which is never killed on Windows), lock screens, the login managers,
    // polkit and keyring agents, and Be Productive itself ("dotnet" for development runs).
    private static readonly HashSet<string> NeverKill = new(StringComparer.OrdinalIgnoreCase)
    {
        "systemd", "dbus-daemon", "dbus-broker", "dbus-broker-launch", "xorg", "xwayland", "x", "xinit",
        "gnome-shell", "gnome-session-binary", "gnome-session", "gnome-session-ctl", "mutter",
        "plasmashell", "kwin_x11", "kwin_wayland", "kwin_wayland_wrapper", "ksmserver", "kded5", "kded6",
        "kscreenlocker_greet", "kglobalaccel", "kactivitymanagerd", "startplasma-x11", "startplasma-wayland",
        "xfce4-session", "xfwm4", "xfce4-panel", "xfdesktop", "xfsettingsd", "cinnamon", "cinnamon-session",
        "mate-session", "marco", "mate-panel", "budgie-wm", "budgie-panel", "lxqt-session", "openbox",
        "sway", "hyprland", "river", "labwc", "wayfire", "i3", "bspwm", "awesome", "waybar", "swaybg",
        "nautilus", "dolphin", "thunar", "nemo", "caja", "pcmanfm", "pcmanfm-qt",
        "swaylock", "i3lock", "hyprlock", "xsecurelock", "light-locker", "xscreensaver", "gnome-screensaver",
        "gdm", "gdm-session-worker", "gdm-wayland-session", "gdm-x-session", "sddm", "sddm-helper", "lightdm",
        "polkit-gnome-authentication-agent-1", "polkit-kde-authentication-agent-1", "lxpolkit",
        "gnome-keyring-daemon", "kwalletd5", "kwalletd6", "ssh-agent", "gpg-agent",
        "pipewire", "pipewire-pulse", "wireplumber", "pulseaudio",
        "ibus-daemon", "fcitx5", "fcitx",
        "beproductive", "beproductive.service", "bp", "dotnet",
    };

    // Prefixes of helper families: GNOME settings daemons, portals, accessibility, GVfs, and so on.
    private static readonly string[] NeverKillPrefixes =
    [
        "gsd-", "xdg-", "at-spi", "gvfs", "evolution-", "tracker-", "localsearch", "goa-", "ibus-", "kde",
        "org_kde", "plasma-", "xfce4-notifyd", "notification-daemon", "dunst", "mako",
    ];

    private readonly int self = Environment.ProcessId;

    public IReadOnlyList<RunningProcess> UserProcesses()
    {
        // Nobody at the screen (the login screen, or a text console): police nothing.
        if (sessions.ActiveGraphicalSession() is not { } session || session.Uid == 0)
            return [];

        var candidates = Candidates();
        var result = new List<RunningProcess>();
        foreach (var process in proc.Processes())
        {
            if (IsPoliceable(process, session.Uid, candidates))
                result.Add(new RunningProcess(process.Pid, process.Name));
        }

        return result;
    }

    public bool Kill(int pid)
    {
        if (sessions.ActiveGraphicalSession() is not { } session || session.Uid == 0)
            return false;

        // Look again: the process may have ended and its id been reused since it was listed.
        if (proc.Read(pid) is not { } process || !IsPoliceable(process, session.Uid, Candidates()))
            return false;
        try
        {
            using var running = System.Diagnostics.Process.GetProcessById(pid);
            running.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsPoliceable(LinuxProcess process, int uid, IReadOnlySet<string> candidates) =>
        process.Uid == uid &&
        process.Pid != self &&
        !IsProtected(process.Name) &&
        candidates.Contains(process.Name);

    internal static bool IsProtected(string name) =>
        NeverKill.Contains(name) || NeverKillPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private HashSet<string> Candidates()
    {
        var value = options.Value;
        var names = new HashSet<string>(desktopApps.Names(), StringComparer.OrdinalIgnoreCase);
        foreach (var name in value.TerminalApps.Concat(value.AdminToolApps).Append(value.BrowserApp))
            names.Add(EnforcementPlanner.Normalize(name));
        return names;
    }
}
