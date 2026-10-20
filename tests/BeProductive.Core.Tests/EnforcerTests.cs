using BeProductive.Core.Enforcement;
using BeProductive.Core.SafetyNet;
using BeProductive.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BeProductive.Core.Tests;

public sealed class EnforcerTests : IDisposable
{
    private const string ChromeKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google\Chrome";

    private readonly TempDirectory directory = new();
    private readonly FakeRegistry registry = new();
    private readonly FakeHostsFile hosts = new("127.0.0.1 localhost\r\n");
    private readonly FakeDnsCache dns = new();
    private readonly InMemorySessionStore store = new();
    private readonly FakeProcessGuard processes = new();
    private readonly EnforcementOptions options = new()
    {
        DryRun = false,
        GlobalBlockedDomains = ["youtube.com"],
        AlwaysAllowedApps = ["BeProductive"],
        BrowserApp = "chrome",
        TerminalApps = ["powershell", "cmd"],
        ReassertInterval = TimeSpan.FromSeconds(20),
    };

    private readonly FakeTimeProvider clock = new(EngineHarness.Tuesday9am);
    private DateTimeOffset now = EngineHarness.Tuesday9am;

    public EnforcerTests()
    {
        registry.CreateKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google");
        store.Find("programming")!.AllowedApps = ["Code"];
    }

    public void Dispose() => directory.Dispose();

    private Enforcer Create(bool dryRun = false, bool panicFileEnabled = true)
    {
        options.DryRun = dryRun;
        var changes = new SystemChanges(
            new JsonJournalStore(directory.AppPaths), registry, hosts, dns,
            Gates.Create(directory.AppPaths, dryRun, panicFileEnabled),
            Options.Create(new SafetyNetOptions()), clock, NullLogger<SystemChanges>.Instance);
        return new Enforcer(
            changes, new TestPolicies(), processes, Gates.Create(directory.AppPaths, dryRun, panicFileEnabled),
            store, store, Options.Create(options), Options.Create(new SafetyNetOptions()), NullLogger<Enforcer>.Instance);
    }

    private SessionStatus Focus(bool admin = true) => Status(Restriction.Focus, admin);

    private SessionStatus Status(Restriction restriction, bool admin) => new(
        new DateOnly(2026, 10, 20), Phase.Focus, DayOutcome.Open, restriction, admin,
        restriction == Restriction.None ? null : "programming", "Programming",
        100, 120, 0, TimeSpan.Zero, 0, null, null, false, null, null, 3, now.AddHours(11));

    [Fact]
    public void Focus_blocks_domains_sets_chrome_policies_and_kills_disallowed_apps()
    {
        processes.Running = [new(10, "code"), new(20, "steam"), new(30, "beproductive"), new(40, "chrome")];
        var enforcer = Create();

        enforcer.Apply(Focus(), now);

        Assert.Contains("0.0.0.0 youtube.com", hosts.Content);
        Assert.Equal((PolicyValueKind.String, "off"), registry.Values[(ChromeKey, "DnsOverHttpsMode")]);
        Assert.Equal([20], processes.Killed);
        Assert.Equal("steam", Assert.Single(store.Events).Detail);
    }

    [Fact]
    public void A_dry_run_changes_nothing_but_still_logs_the_intended_block()
    {
        processes.Running = [new(20, "steam")];
        var enforcer = Create(dryRun: true);

        enforcer.Apply(Focus(), now);

        Assert.Equal("127.0.0.1 localhost\r\n", hosts.Content);
        Assert.Empty(registry.Values);
        Assert.Empty(processes.Killed);
        Assert.Equal("steam", Assert.Single(store.Events).Detail);
    }

    [Fact]
    public void The_panic_file_undoes_changes_and_stops_killing()
    {
        processes.Running = [new(20, "steam")];
        var enforcer = Create();
        enforcer.Apply(Focus(), now);
        Assert.NotEmpty(registry.Values);

        File.WriteAllText(directory.AppPaths.PanicFile, "");
        processes.Killed.Clear();
        Advance(TimeSpan.FromMinutes(1));
        enforcer.Apply(Focus(), now);

        Assert.Empty(registry.Values);
        Assert.Equal("127.0.0.1 localhost\r\n", hosts.Content);
        Assert.Empty(processes.Killed);
    }

    [Fact]
    public void Ending_focus_undoes_every_machine_change()
    {
        var enforcer = Create();
        enforcer.Apply(Focus(), now);
        Assert.Contains("youtube.com", hosts.Content);

        enforcer.Apply(Status(Restriction.None, admin: false), now);

        Assert.DoesNotContain("youtube.com", hosts.Content);
        Assert.Empty(registry.Values);
        Assert.False(File.Exists(directory.AppPaths.JournalFile));
    }

    [Fact]
    public void A_break_clears_the_domains_but_keeps_admin_policies_and_kills_only_terminals()
    {
        var enforcer = Create();
        enforcer.Apply(Focus(), now);

        processes.Running = [new(50, "powershell"), new(60, "spotify")];
        Advance(TimeSpan.FromMinutes(25));
        enforcer.Apply(Status(Restriction.Break, admin: true), now);

        Assert.DoesNotContain("youtube.com", hosts.Content);
        Assert.Equal((PolicyValueKind.DWord, "1"), registry.Values[(AdminKey, "DisableTaskMgr")]);
        Assert.Equal([50], processes.Killed);
    }

    [Fact]
    public void Policies_and_hosts_are_not_rewritten_every_tick_but_are_reasserted_after_the_interval()
    {
        var enforcer = Create();
        enforcer.Apply(Focus(), now);
        var writes = hosts.Writes;

        Advance(TimeSpan.FromSeconds(5));
        enforcer.Apply(Focus(), now);
        Assert.Equal(writes, hosts.Writes);

        Advance(TimeSpan.FromSeconds(20));
        enforcer.Apply(Focus(), now);
        Assert.Equal(writes + 1, hosts.Writes);
    }

    [Fact]
    public void The_same_blocked_app_is_logged_at_most_once_a_minute()
    {
        var enforcer = Create();

        processes.Running = [new(20, "steam")];
        enforcer.Apply(Focus(), now);
        Advance(TimeSpan.FromSeconds(10));
        processes.Running = [new(21, "steam")]; // relaunched under a new pid
        enforcer.Apply(Focus(), now);
        Assert.Single(store.Events);

        Advance(TimeSpan.FromMinutes(1));
        processes.Running = [new(22, "steam")];
        enforcer.Apply(Focus(), now);
        Assert.Equal(2, store.Events.Count);
    }

    [Fact]
    public void The_lease_is_renewed_so_a_healthy_session_never_self_restores()
    {
        var enforcer = Create();
        enforcer.Apply(Focus(), now);

        Advance(TimeSpan.FromMinutes(20));
        enforcer.Apply(Focus(), now);

        Assert.Contains("youtube.com", hosts.Content);
    }

    private void Advance(TimeSpan by)
    {
        clock.Advance(by);
        now += by;
    }

    private const string AdminKey = @"HKEY_USERS\S-1-5-21-test\Software\Microsoft\Windows\CurrentVersion\Policies\System";

    private sealed class TestPolicies : IEnforcementPolicies
    {
        public IReadOnlyList<PolicyValue> For(PolicyGroup group) => group switch
        {
            PolicyGroup.Chrome => [new(ChromeKey, "DnsOverHttpsMode", PolicyValueKind.String, "off")],
            PolicyGroup.AdminTools => [new(AdminKey, "DisableTaskMgr", PolicyValueKind.DWord, "1")],
            _ => [],
        };
    }
}

internal sealed class FakeProcessGuard : IProcessGuard
{
    public List<RunningProcess> Running { get; set; } = [];

    public List<int> Killed { get; } = [];

    public IReadOnlyList<RunningProcess> UserProcesses() => Running;

    public bool Kill(int pid)
    {
        Killed.Add(pid);
        Running.RemoveAll(process => process.Pid == pid);
        return true;
    }
}
