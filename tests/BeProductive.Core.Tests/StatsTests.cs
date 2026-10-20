using BeProductive.Core.Sessions;

namespace BeProductive.Core.Tests;

public class StatsTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact]
    public void Streaks_count_days_on_target_in_a_row()
    {
        var days = new (DateOnly, DayOutcome)[]
        {
            (Today.AddDays(-6), DayOutcome.Done),
            (Today.AddDays(-5), DayOutcome.Done),
            (Today.AddDays(-4), DayOutcome.Done),
            (Today.AddDays(-3), DayOutcome.Missed),
            (Today.AddDays(-2), DayOutcome.Done),
            (Today.AddDays(-1), DayOutcome.NoMinimum),
        };

        Assert.Equal((2, 3), Streaks.From(days, Today));
    }

    [Fact]
    public void Today_in_progress_neither_extends_nor_breaks_a_streak()
    {
        var days = new (DateOnly, DayOutcome)[] { (Today.AddDays(-1), DayOutcome.Done), (Today, DayOutcome.Open) };

        Assert.Equal((1, 1), Streaks.From(days, Today));
    }

    [Fact]
    public void A_failed_day_breaks_the_streak_and_missing_days_are_skipped()
    {
        Assert.Equal((0, 1), Streaks.From([(Today.AddDays(-3), DayOutcome.Done), (Today, DayOutcome.Failed)], Today));
        Assert.Equal((2, 2), Streaks.From([(Today.AddDays(-5), DayOutcome.Done), (Today, DayOutcome.Done)], Today));
    }

    [Fact]
    public void Stats_cover_every_day_in_range_with_today_counted_live()
    {
        var harness = new EngineHarness();
        harness.Store.SaveDay(new DayRecord(Today.AddDays(-2), 100, 120, 30, TimeSpan.FromMinutes(150), 6, DayOutcome.Done));
        harness.Store.RecordBlock(new BlockRecord(Today.AddDays(-2), "programming", BlockKind.Long45, false, null,
            harness.Now.AddDays(-2), harness.Now.AddDays(-2).AddMinutes(45), TimeSpan.FromMinutes(45), true));
        harness.Plan(100);
        harness.RunMinutes(25);
        harness.RunMinutes(5);
        harness.Next();
        harness.RunMinutes(10);
        var builder = new StatsBuilder(harness.Store, harness.Store, harness.Options, TimeZoneInfo.Utc);

        var stats = builder.Build(harness.Engine.Snapshot(harness.Now), 7, harness.Now);

        Assert.Equal(7, stats.Days.Count);
        Assert.Equal(Today.AddDays(-6), stats.From);
        Assert.Null(stats.Days[0].Outcome);
        Assert.Equal(new DayStats(Today.AddDays(-2), 100, 150, 150, 6, DayOutcome.Done), stats.Days[4]);
        Assert.Equal(new DayStats(Today, 100, 100, 35, 1, DayOutcome.Open), stats.Days[6]);

        Assert.Equal(["programming", "uni"], stats.Categories.Select(category => category.PresetId));
        Assert.Equal(35, stats.Categories.Single(category => category.PresetId == "uni").FocusedMinutes);
        Assert.Equal("Uni studying", stats.Categories.Single(category => category.PresetId == "uni").Name);
        Assert.Equal((1, 1), (stats.CurrentStreak, stats.BestStreak));
    }

    [Fact]
    public void Stats_count_pauses_postpones_and_recovery_exits_in_range()
    {
        var harness = new EngineHarness();
        harness.Engine.Postpone(harness.Now, harness.Now.AddMinutes(10));
        harness.RunMinutes(10);
        harness.Plan(100);
        harness.Engine.Pause(harness.Now);
        harness.Engine.Resume(harness.Now);
        harness.Engine.EndWithRecoveryCode(harness.Now);
        var builder = new StatsBuilder(harness.Store, harness.Store, harness.Options, TimeZoneInfo.Utc);

        var stats = builder.Build(harness.Engine.Snapshot(harness.Now), 7, harness.Now.AddSeconds(1));

        Assert.Equal((1, 1, 1), (stats.Pauses, stats.Postpones, stats.RecoveryExits));
        Assert.Equal(DayOutcome.Failed, stats.Days[^1].Outcome);
    }
}
