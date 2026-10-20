namespace BeProductive.Core.Enforcement;

public sealed class EnforcementOptions
{
    public const string SectionName = "Enforcement";

    /// <summary>
    /// When true (the default), nothing on the machine is blocked or changed: every action is only logged.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// When true, a file named PANIC in the data directory switches all enforcement off and undoes
    /// whatever is applied. Meant for development builds, so a bug can't lock you out while testing.
    /// </summary>
    public bool PanicFileEnabled { get; set; } = true;

    /// <summary>Domains blocked during focus in every preset, on top of each preset's own list.</summary>
    public List<string> GlobalBlockedDomains { get; set; } =
    [
        "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be",
        "reddit.com", "www.reddit.com",
        "instagram.com", "www.instagram.com",
        "tiktok.com", "www.tiktok.com",
        "twitter.com", "x.com", "www.x.com",
        "facebook.com", "www.facebook.com",
        "netflix.com", "www.netflix.com",
        "twitch.tv", "www.twitch.tv",
    ];

    /// <summary>
    /// Programs allowed during focus in every preset, on top of each preset's own list. Program names,
    /// with or without ".exe". Background-noise players and most built-in tools belong here; a music app
    /// like Spotify does not — it goes in the presets that want it.
    /// </summary>
    public List<string> AlwaysAllowedApps { get; set; } =
    [
        "BeProductive", "SystemSettings", "Calculator", "CalculatorApp",
        "SnippingTool", "ScreenClippingHost", "Notepad",
    ];

    /// <summary>The browser allowed during focus. Every other browser is treated as not-allowed.</summary>
    public string BrowserApp { get; set; } = "chrome";

    /// <summary>
    /// Terminal and shell programs. Allowed only in a preset that turns terminals on; otherwise blocked
    /// all day while admin tools are blocked, breaks included.
    /// </summary>
    public List<string> TerminalApps { get; set; } =
    [
        "WindowsTerminal", "powershell", "pwsh", "cmd", "conhost",
    ];

    /// <summary>How often applied policies and the hosts file are written again, to undo tampering.</summary>
    public TimeSpan ReassertInterval { get; set; } = TimeSpan.FromSeconds(20);
}
