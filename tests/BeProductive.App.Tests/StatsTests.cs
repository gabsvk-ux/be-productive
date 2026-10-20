using Avalonia.Headless.XUnit;
using BeProductive.App.ViewModels;
using BeProductive.App.Views;
using BeProductive.Core.Sessions;

namespace BeProductive.App.Tests;

public class StatsTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    /// <summary>A believable history: mostly on target, a missed day, a free Sunday, today in progress.</summary>
    private static StatsSnapshot Sample(int days)
    {
        var random = new Random(7);
        var list = new List<DayStats>();
        for (var i = days - 1; i >= 0; i--)
        {
            var day = Today.AddDays(-i);
            if (i == 0)
                list.Add(new DayStats(day, 100, 120, 70, 2, DayOutcome.Open));
            else if (day.DayOfWeek == DayOfWeek.Sunday)
                list.Add(new DayStats(day, 0, 0, random.Next(0, 40), 1, DayOutcome.NoMinimum));
            else if (i % 9 == 4)
                list.Add(new DayStats(day, 100, 100, 50, 2, DayOutcome.Missed));
            else if (i % 13 == 6)
                list.Add(new DayStats(day, 0, 0, 0, 0, null));
            else
            {
                var goal = 100 + random.Next(0, 4) * 20;
                list.Add(new DayStats(day, 100, goal, goal + random.Next(0, 30), goal / 25, DayOutcome.Done));
            }
        }

        var total = list.Sum(day => day.FocusedMinutes);
        return new StatsSnapshot(list[0].Day, Today, list,
            [
                new("uni", "Uni studying", total * 55 / 100, 30),
                new("programming", "Programming", total * 30 / 100, 12),
                new("projects", "Projects", total * 15 / 100, 6),
            ],
            CurrentStreak: 3, BestStreak: 9, Pauses: 4, Postpones: 2, RecoveryExits: 1, BlockedApps: 23);
    }

    private static async Task<StatsViewModel> OpenAsync()
    {
        var service = new FakeServiceClient { Status = Statuses.Focus(), Stats = Sample };
        var app = new AppViewModel(service, new FixedTime(FakeServiceClient.Now));
        await app.RefreshAsync();
        var stats = new StatsViewModel(app);
        await stats.LoadAsync();
        return stats;
    }

    [AvaloniaFact]
    public async Task The_week_view_shows_totals_a_column_per_day_and_presets()
    {
        var stats = await OpenAsync();
        Screenshots.Save(new StatsWindow { DataContext = stats }, "stats-week", 900, 760);
        Screenshots.Save(new StatsWindow { DataContext = stats, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark }, "stats-week-dark", 900, 760);

        Assert.Equal(7, stats.Columns.Count);
        Assert.Equal("Tu", stats.Columns[^1].Label);
        Assert.Equal(70, stats.Columns[^1].FocusedMinutes);
        Assert.Equal(["Uni studying", "Programming", "Projects"], stats.Categories.Select(bar => bar.Name));
        Assert.True(stats.Categories[0].BarWidth > stats.Categories[1].BarWidth);
        Assert.Equal("3 days", stats.Streak);
        Assert.Equal(7, stats.Rows.Count);
        Assert.Equal("… In progress", stats.Rows[0].Outcome);
    }

    [AvaloniaFact]
    public async Task The_month_view_labels_columns_by_date()
    {
        var stats = await OpenAsync();

        stats.SelectedRange = StatsViewModel.Ranges[1];
        await stats.LoadAsync();
        Screenshots.Save(new StatsWindow { DataContext = stats }, "stats-month", 900, 760);

        Assert.Equal(30, stats.Columns.Count);
        Assert.Equal("20", stats.Columns[^1].Label);
        Assert.Contains(stats.Rows, row => row.Outcome == "✕ Missed");
    }

    [AvaloniaFact]
    public async Task The_year_view_adds_up_months()
    {
        var stats = await OpenAsync();

        stats.SelectedRange = StatsViewModel.Ranges[2];
        await stats.LoadAsync();
        Screenshots.Save(new StatsWindow { DataContext = stats }, "stats-year", 900, 760);

        Assert.Equal("Focus per month", stats.ChartTitle);
        Assert.InRange(stats.Columns.Count, 12, 13);
        Assert.Equal(stats.Stats!.Days.Sum(day => day.FocusedMinutes), stats.Columns.Sum(column => column.FocusedMinutes));
        Assert.DoesNotContain(stats.Rows, row => row.Outcome == "– Nothing recorded");
    }
}
