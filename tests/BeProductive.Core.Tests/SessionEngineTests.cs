using BeProductive.Core.Sessions;

namespace BeProductive.Core.Tests;

public class SessionEngineTests
{
    [Fact]
    public void A_day_with_a_minimum_starts_at_the_gate_with_everything_locked()
    {
        var harness = new EngineHarness();

        Assert.Equal(Phase.Gate, harness.State.Phase);
        Assert.Equal(DayOutcome.Open, harness.State.Outcome);
        Assert.Equal(100, harness.State.MinimumMinutes);
        Assert.Equal(Restriction.Gate, harness.Engine.Restriction);
        Assert.True(harness.Engine.AdminToolsBlocked);
    }

    [Fact]
    public void A_day_without_a_minimum_is_free()
    {
        var harness = new EngineHarness(new SessionOptions { MinimumMinutes = new WeekdayMinimums { Tuesday = 0 } });

        Assert.Equal(Phase.Free, harness.State.Phase);
        Assert.Equal(DayOutcome.NoMinimum, harness.State.Outcome);
        Assert.False(harness.Engine.AdminToolsBlocked);
    }

    [Fact]
    public void Planning_needs_a_known_preset_and_at_least_the_minimum()
    {
        var harness = new EngineHarness();

        Assert.Throws<SessionRuleException>(() => harness.Plan(90));
        Assert.Throws<SessionRuleException>(() => harness.Plan(120, preset: "games"));

        harness.Plan(120);
        Assert.Equal(Phase.Focus, harness.State.Phase);
        Assert.Equal(120, harness.State.BindingMinutes);
        Assert.Equal(Restriction.Focus, harness.Engine.Restriction);
        Assert.Throws<SessionRuleException>(() => harness.Plan(120));
    }

    [Fact]
    public void A_minimum_day_is_four_25_minute_blocks_with_short_breaks_and_no_long_break()
    {
        var harness = new EngineHarness();
        harness.Plan(100);

        harness.RunMinutes(25);
        Assert.Equal(Phase.Break, harness.State.Phase);
        Assert.False(harness.State.LongBreak);
        Assert.Equal(harness.Now.AddMinutes(5), harness.State.BreakEndsAt);
        Assert.Equal(Restriction.Break, harness.Engine.Restriction);
        Assert.True(harness.Engine.AdminToolsBlocked);

        harness.RunMinutes(5);
        Assert.Equal(Phase.AwaitingNext, harness.State.Phase);
        harness.Next();

        harness.FinishBlockAndBreak();
        harness.FinishBlockAndBreak();
        harness.RunMinutes(25);

        Assert.Equal(Phase.Free, harness.State.Phase);
        Assert.Equal(DayOutcome.Done, harness.State.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(100), harness.State.Focused);
        Assert.Equal(4, harness.State.CompletedBlocks);
        Assert.False(harness.Engine.AdminToolsBlocked);
        Assert.Equal(DayOutcome.Done, harness.Store.DayRecords[harness.State.Day].Outcome);
        Assert.Equal(4, harness.Store.Blocks.Count(block => block.Completed));
    }

    [Fact]
    public void A_long_break_follows_every_fourth_block_when_more_is_planned()
    {
        var harness = new EngineHarness();
        harness.Plan(150);

        harness.FinishBlockAndBreak();
        harness.FinishBlockAndBreak();
        harness.FinishBlockAndBreak();
        harness.RunMinutes(25);

        Assert.Equal(Phase.Break, harness.State.Phase);
        Assert.True(harness.State.LongBreak);
        Assert.Equal(harness.Now.AddMinutes(20), harness.State.BreakEndsAt);
    }

    [Fact]
    public void A_block_that_starts_before_the_target_runs_to_its_end()
    {
        var harness = new EngineHarness();
        harness.Plan(100, BlockKind.Long45);

        harness.FinishBlockAndBreak(BlockKind.Long45);
        harness.FinishBlockAndBreak(BlockKind.Long45);
        Assert.Equal(Phase.Focus, harness.State.Phase);

        harness.RunMinutes(30);
        Assert.Equal(Phase.Focus, harness.State.Phase);

        harness.RunMinutes(15);
        Assert.Equal(Phase.Free, harness.State.Phase);
        Assert.Equal(TimeSpan.FromMinutes(135), harness.State.Focused);
    }

    [Fact]
    public void Paused_time_does_not_count_and_a_pause_ends_on_its_own()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        harness.RunMinutes(10);

        harness.Engine.Pause(harness.Now);
        harness.RunMinutes(5);
        Assert.Equal(TimeSpan.FromMinutes(10), harness.State.Block!.Focused);
        Assert.Equal(Restriction.Focus, harness.Engine.Restriction);

        harness.RunMinutes(6);
        Assert.Null(harness.State.Block!.PausedSince);
        Assert.Contains(harness.Store.Events, e => e.Kind == SessionEventKinds.AutoResume);
    }

    [Fact]
    public void Breaks_cannot_be_paused()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        harness.RunMinutes(25);

        Assert.Equal(Phase.Break, harness.State.Phase);
        Assert.Throws<SessionRuleException>(() => harness.Engine.Pause(harness.Now));
    }

    [Fact]
    public void After_a_break_the_presets_default_block_starts_if_none_is_picked()
    {
        var harness = new EngineHarness();
        harness.Plan(200, preset: "programming");
        harness.RunMinutes(25);
        harness.RunMinutes(5);
        Assert.Equal(Phase.AwaitingNext, harness.State.Phase);

        harness.Run(TimeSpan.FromSeconds(65));

        Assert.Equal(Phase.Focus, harness.State.Phase);
        Assert.Equal(BlockKind.Long45, harness.State.Block!.Kind);
    }

    [Fact]
    public void Presets_can_only_be_switched_during_a_break()
    {
        var harness = new EngineHarness();
        harness.Plan(100);

        Assert.Throws<SessionRuleException>(() => harness.Engine.SwitchPreset(harness.Now, "programming"));

        harness.RunMinutes(25);
        harness.Engine.SwitchPreset(harness.Now, "programming");
        harness.RunMinutes(5);
        harness.Next();

        Assert.Equal("programming", harness.State.Block!.PresetId);
        Assert.Equal("uni", harness.Store.Blocks[0].PresetId);
    }

    [Fact]
    public void Postponing_moves_the_gate_to_a_later_time()
    {
        var harness = new EngineHarness();

        harness.Engine.Postpone(harness.Now, harness.Now.AddHours(2));
        Assert.Equal(Phase.Postponed, harness.State.Phase);
        Assert.Equal(Restriction.None, harness.Engine.Restriction);
        Assert.False(harness.Engine.AdminToolsBlocked);

        harness.Run(TimeSpan.FromHours(2));
        Assert.Equal(Phase.Gate, harness.State.Phase);
    }

    [Fact]
    public void Postponing_has_a_latest_start_and_must_be_in_the_future()
    {
        var harness = new EngineHarness();

        Assert.Throws<SessionRuleException>(() => harness.Engine.Postpone(harness.Now, harness.Now.AddMinutes(-1)));
        Assert.Throws<SessionRuleException>(() => harness.Engine.Postpone(harness.Now, new DateTimeOffset(2026, 10, 20, 20, 1, 0, TimeSpan.Zero)));
        harness.Engine.Postpone(harness.Now, new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Only_three_postpones_are_allowed_per_week_and_the_count_resets_on_monday()
    {
        var harness = new EngineHarness();
        for (var i = 0; i < 3; i++)
        {
            harness.Engine.Postpone(harness.Now, harness.Now.AddMinutes(10));
            harness.RunMinutes(10);
        }

        Assert.Equal(0, harness.Engine.Snapshot(harness.Now).PostponesLeft);
        Assert.Throws<SessionRuleException>(() => harness.Engine.Postpone(harness.Now, harness.Now.AddMinutes(10)));

        // Next Monday 09:00, six days later.
        harness.Sleep(TimeSpan.FromDays(6) - TimeSpan.FromMinutes(30));
        Assert.Equal(DayOfWeek.Monday, harness.State.Day.DayOfWeek);
        Assert.Equal(3, harness.Engine.Snapshot(harness.Now).PostponesLeft);
    }

    [Fact]
    public void A_short_sleep_resumes_the_block_without_counting_the_sleep()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        harness.RunMinutes(10);

        harness.Sleep(TimeSpan.FromMinutes(20));

        Assert.Equal(Phase.Focus, harness.State.Phase);
        Assert.Equal(TimeSpan.FromMinutes(10), harness.State.Block!.Focused);
        harness.RunMinutes(15);
        Assert.Equal(Phase.Break, harness.State.Phase);
    }

    [Fact]
    public void A_long_sleep_closes_the_block_and_keeps_its_focus_time()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        harness.RunMinutes(10);

        harness.Sleep(TimeSpan.FromMinutes(45));

        Assert.Equal(Phase.AwaitingNext, harness.State.Phase);
        Assert.Equal(TimeSpan.FromMinutes(10), harness.State.Focused);
        Assert.Equal(0, harness.State.CompletedBlocks);
        Assert.False(Assert.Single(harness.Store.Blocks).Completed);
    }

    [Fact]
    public void A_service_restart_carries_on_where_it_left_off()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        harness.RunMinutes(10);

        harness.Restart(TimeSpan.FromMinutes(2));

        Assert.Equal(Phase.Focus, harness.State.Phase);
        Assert.Equal(TimeSpan.FromMinutes(10), harness.State.Block!.Focused);
        harness.RunMinutes(15);
        Assert.Equal(Phase.Break, harness.State.Phase);
    }

    [Fact]
    public void An_unfinished_day_is_recorded_as_missed_at_four_in_the_morning()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        harness.RunMinutes(25);

        harness.Sleep(TimeSpan.FromHours(19));

        Assert.Equal(new DateOnly(2026, 10, 21), harness.State.Day);
        Assert.Equal(Phase.Gate, harness.State.Phase);
        var tuesday = harness.Store.DayRecords[new DateOnly(2026, 10, 20)];
        Assert.Equal(DayOutcome.Missed, tuesday.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(25), tuesday.Focused);
    }

    [Fact]
    public void A_late_night_session_still_counts_for_the_day_before()
    {
        var harness = new EngineHarness(start: new DateTimeOffset(2026, 10, 21, 1, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 20), harness.State.Day);
    }

    [Fact]
    public void Extra_time_after_the_planned_time_is_soft()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        for (var i = 0; i < 3; i++)
            harness.FinishBlockAndBreak();
        harness.RunMinutes(25);
        Assert.Equal(Phase.Free, harness.State.Phase);

        harness.Engine.Extend(harness.Now, 60, BlockKind.Classic25, presetId: null, note: null);

        Assert.Equal(Phase.Focus, harness.State.Phase);
        Assert.True(harness.State.Block!.Extra);
        Assert.Equal(Restriction.Focus, harness.Engine.Restriction);
        Assert.False(harness.Engine.AdminToolsBlocked);

        harness.RunMinutes(10);
        harness.Engine.StopExtra(harness.Now);
        Assert.Equal(Phase.Free, harness.State.Phase);
        Assert.Equal(TimeSpan.FromMinutes(110), harness.State.Focused);
        Assert.Equal(DayOutcome.Done, harness.State.Outcome);
    }

    [Fact]
    public void Planned_time_cannot_be_stopped_or_extended_early()
    {
        var harness = new EngineHarness();
        harness.Plan(100);

        Assert.Throws<SessionRuleException>(() => harness.Engine.StopExtra(harness.Now));
        Assert.Throws<SessionRuleException>(() => harness.Engine.Extend(harness.Now, 30, BlockKind.Classic25, null, null));
    }

    [Fact]
    public void Extra_time_runs_its_own_blocks_and_breaks_until_it_is_used_up()
    {
        var harness = new EngineHarness(new SessionOptions { MinimumMinutes = new WeekdayMinimums { Tuesday = 0 } });

        harness.Engine.Extend(harness.Now, 50, BlockKind.Classic25, "uni", null);
        harness.FinishBlockAndBreak();
        harness.RunMinutes(25);

        Assert.Equal(Phase.Free, harness.State.Phase);
        Assert.Equal(TimeSpan.FromMinutes(50), harness.State.Focused);
    }

    [Fact]
    public void A_recovery_exit_ends_the_lock_and_fails_the_day()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        harness.RunMinutes(10);

        harness.Engine.EndWithRecoveryCode(harness.Now);

        Assert.Equal(Phase.Free, harness.State.Phase);
        Assert.Equal(DayOutcome.Failed, harness.State.Outcome);
        Assert.False(harness.Engine.AdminToolsBlocked);
        Assert.Equal(TimeSpan.FromMinutes(10), harness.State.Focused);
        Assert.Equal(DayOutcome.Failed, harness.Store.DayRecords[harness.State.Day].Outcome);
        Assert.Throws<SessionRuleException>(() => harness.Engine.EnsureRecoveryExitPossible());
    }

    [Fact]
    public void A_recovery_exit_is_refused_when_nothing_is_locked()
    {
        var harness = new EngineHarness();
        harness.Engine.Postpone(harness.Now, harness.Now.AddHours(1));

        Assert.Throws<SessionRuleException>(() => harness.Engine.EnsureRecoveryExitPossible());
    }

    [Fact]
    public void The_status_includes_the_running_blocks_focus()
    {
        var harness = new EngineHarness();
        harness.Plan(100);
        harness.RunMinutes(10);

        var status = harness.Engine.Snapshot(harness.Now);

        Assert.Equal(TimeSpan.FromMinutes(10), status.FocusedToday);
        Assert.Equal(TimeSpan.FromMinutes(15), status.Block!.Remaining);
        Assert.Equal("Uni studying", status.PresetName);
    }
}
