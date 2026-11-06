using BeProductive.Core.Enforcement;
using BeProductive.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.Tests;

public sealed class WatchdogTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly InMemorySessionStore store = new();
    private readonly FakeAppLauncher launcher = new();
    private DateTimeOffset now = EngineHarness.Tuesday9am;

    public void Dispose() => directory.Dispose();

    private Watchdog Create(bool dryRun = false, bool panicFileEnabled = true) =>
        new(launcher, Gates.Create(directory.AppPaths, dryRun, panicFileEnabled), store,
            Options.Create(new WatchdogOptions { RelaunchBackoff = TimeSpan.FromSeconds(5) }), NullLogger<Watchdog>.Instance);

    private SessionStatus Status(Restriction restriction, bool admin = true) => new(
        new DateOnly(2026, 10, 20), Phase.Focus, DayOutcome.Open, restriction, admin,
        "uni", "Uni studying", 100, 120, 0, TimeSpan.Zero, 0, null, null, false, null, null, 3, now.AddHours(11));

    private void Advance(TimeSpan by) => now += by;

    [Fact]
    public void A_free_laptop_does_not_police_the_app()
    {
        launcher.Running = false;
        Create().Tick(Status(Restriction.None, admin: false), now);

        Assert.Equal(0, launcher.Launches);
        Assert.Empty(store.Events);
    }

    [Fact]
    public void A_missing_app_is_launched_while_locked()
    {
        launcher.Running = false;
        Create().Tick(Status(Restriction.Gate), now);

        Assert.Equal(1, launcher.Launches);
    }

    [Fact]
    public void Killing_the_app_relaunches_it_and_records_a_tamper_attempt()
    {
        var watchdog = Create();
        launcher.Running = true;
        watchdog.Tick(Status(Restriction.Focus), now); // seen running
        Assert.Equal(0, launcher.Launches);

        launcher.Running = false;
        Advance(TimeSpan.FromSeconds(3));
        watchdog.Tick(Status(Restriction.Focus), now);

        Assert.Equal(1, launcher.Launches);
        var tamper = Assert.Single(store.Events);
        Assert.Equal(SessionEventKinds.TamperAttempt, tamper.Kind);
        Assert.Equal("app-closed", tamper.Detail);
    }

    [Fact]
    public void A_first_launch_without_ever_seeing_the_app_is_not_a_tamper_attempt()
    {
        launcher.Running = false;
        Create().Tick(Status(Restriction.Focus), now);

        Assert.Equal(1, launcher.Launches);
        Assert.Empty(store.Events);
    }

    [Fact]
    public void Relaunching_is_backed_off_so_a_crash_loop_is_not_hammered()
    {
        var watchdog = Create();
        launcher.LaunchKeepsRunning = false; // the app dies again right after each launch
        launcher.Running = false;

        watchdog.Tick(Status(Restriction.Focus), now);
        Advance(TimeSpan.FromSeconds(2));
        watchdog.Tick(Status(Restriction.Focus), now);
        Assert.Equal(1, launcher.Launches); // still inside the 5s backoff

        Advance(TimeSpan.FromSeconds(4));
        watchdog.Tick(Status(Restriction.Focus), now);
        Assert.Equal(2, launcher.Launches); // backoff elapsed, tries again
    }

    [Fact]
    public void Dry_run_and_panic_only_log_and_never_launch()
    {
        launcher.Running = false;
        Create(dryRun: true).Tick(Status(Restriction.Focus), now);
        Assert.Equal(0, launcher.Launches);

        File.WriteAllText(directory.AppPaths.PanicFile, "");
        Advance(TimeSpan.FromSeconds(10));
        Create().Tick(Status(Restriction.Focus), now);
        Assert.Equal(0, launcher.Launches);
    }

    [Fact]
    public void Repeated_kills_are_logged_at_most_once_every_30_seconds()
    {
        var watchdog = Create();

        launcher.Running = true;
        watchdog.Tick(Status(Restriction.Focus), now);
        launcher.Running = false;
        Advance(TimeSpan.FromSeconds(3));
        watchdog.Tick(Status(Restriction.Focus), now);
        Assert.Single(store.Events);

        // It's relaunched and killed again within the window: still one tamper event.
        launcher.Running = true;
        Advance(TimeSpan.FromSeconds(6));
        watchdog.Tick(Status(Restriction.Focus), now);
        launcher.Running = false;
        Advance(TimeSpan.FromSeconds(6));
        watchdog.Tick(Status(Restriction.Focus), now);
        Assert.Single(store.Events);

        // After the window, a new kill is logged again.
        launcher.Running = true;
        watchdog.Tick(Status(Restriction.Focus), now);
        launcher.Running = false;
        Advance(TimeSpan.FromSeconds(31));
        watchdog.Tick(Status(Restriction.Focus), now);
        Assert.Equal(2, store.Events.Count);
    }
}

internal sealed class FakeAppLauncher : IAppLauncher
{
    public bool Running { get; set; }

    public int Launches { get; private set; }

    public bool IsRunning() => Running;

    /// <summary>When false, a launch doesn't keep the app up (simulates an immediate crash loop).</summary>
    public bool LaunchKeepsRunning { get; set; } = true;

    public bool TryLaunch()
    {
        Launches++;
        Running = LaunchKeepsRunning;
        return true;
    }
}
