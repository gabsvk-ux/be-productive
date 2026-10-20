using BeProductive.Core.Enforcement;
using BeProductive.Core.Sessions;

namespace BeProductive.Core.Tests;

public class EnforcementPlannerTests
{
    private readonly EnforcementOptions options = new()
    {
        GlobalBlockedDomains = ["youtube.com", "reddit.com"],
        AlwaysAllowedApps = ["BeProductive", "Notepad"],
        BrowserApp = "chrome",
        TerminalApps = ["WindowsTerminal", "powershell", "cmd"],
    };

    private EnforcementPlan Build(SessionStatus status, Preset? preset) => new EnforcementPlanner(options).Build(status, preset);

    private static Preset Preset(bool terminals = false, bool elevated = false) => new()
    {
        Id = "programming",
        Name = "Programming",
        AllowedApps = ["Code.exe", "Spotify"],
        BlockedDomains = ["news.ycombinator.com"],
        AllowTerminal = terminals,
        AllowElevatedTerminal = elevated,
    };

    [Fact]
    public void Free_and_postponed_lock_nothing()
    {
        Assert.False(Build(Status(Restriction.None, admin: false), null).Locked);
    }

    [Fact]
    public void Focus_allows_the_preset_apps_the_browser_and_the_always_allowed_set()
    {
        var plan = Build(Status(Restriction.Focus, admin: true), Preset());

        Assert.True(plan.Locked);
        Assert.Equal(KillMode.AllowlistExceptAllowed, plan.KillMode);
        Assert.True(plan.AllowedApps.SetEquals(["beproductive", "notepad", "chrome", "code", "spotify"]));
    }

    [Fact]
    public void Focus_blocks_the_global_and_preset_domains()
    {
        var plan = Build(Status(Restriction.Focus, admin: true), Preset());

        Assert.Contains("youtube.com", plan.BlockedDomains);
        Assert.Contains("reddit.com", plan.BlockedDomains);
        Assert.Contains("news.ycombinator.com", plan.BlockedDomains);
    }

    [Fact]
    public void Terminals_are_allowed_in_focus_only_when_the_preset_allows_them()
    {
        Assert.DoesNotContain("powershell", Build(Status(Restriction.Focus, admin: true), Preset(terminals: false)).AllowedApps);
        Assert.Contains("powershell", Build(Status(Restriction.Focus, admin: true), Preset(terminals: true)).AllowedApps);
    }

    [Fact]
    public void Chrome_policies_are_on_whenever_locked_admin_policies_only_when_admin_tools_are_blocked()
    {
        Assert.Equal([PolicyGroup.Chrome], Build(Status(Restriction.Focus, admin: false), Preset()).Policies);
        Assert.Equal([PolicyGroup.Chrome, PolicyGroup.AdminTools], Build(Status(Restriction.Focus, admin: true), Preset()).Policies.OrderBy(g => g));
    }

    [Fact]
    public void Breaks_do_not_block_domains_but_still_kill_terminals_while_admin_tools_are_blocked()
    {
        var plan = Build(Status(Restriction.Break, admin: true), Preset(terminals: false));

        Assert.Empty(plan.BlockedDomains);
        Assert.Equal(KillMode.BlocklistOnly, plan.KillMode);
        Assert.Contains("cmd", plan.BlockedApps);
        Assert.Contains(PolicyGroup.AdminTools, plan.Policies);
    }

    [Fact]
    public void A_break_in_a_terminal_preset_kills_nothing()
    {
        var plan = Build(Status(Restriction.Break, admin: true), Preset(terminals: true));

        Assert.Equal(KillMode.None, plan.KillMode);
        Assert.Empty(plan.BlockedApps);
    }

    [Fact]
    public void The_gate_keeps_admin_tools_blocked()
    {
        var plan = Build(Status(Restriction.Gate, admin: true), null);

        Assert.True(plan.Locked);
        Assert.Contains(PolicyGroup.AdminTools, plan.Policies);
        Assert.Equal(KillMode.BlocklistOnly, plan.KillMode);
    }

    private static SessionStatus Status(Restriction restriction, bool admin) => new(
        new DateOnly(2026, 10, 20), Phase.Focus, DayOutcome.Open, restriction, admin,
        restriction == Restriction.None ? null : "programming", "Programming",
        100, 120, 0, TimeSpan.Zero, 0, null, null, false, null, null, 3,
        new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero));
}
