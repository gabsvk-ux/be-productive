using System.Collections.ObjectModel;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeProductive.App.ViewModels;

public sealed record StatsRange(string Label, int Days);

/// <summary>One column of the focus chart: a day, or a month in the year view.</summary>
public sealed record ChartColumn(string Label, int FocusedMinutes, int GoalMinutes, string Tooltip);

public sealed record CategoryBar(string Name, string Value, double BarWidth);

public sealed record DayRow(string Day, string Focus, string Goal, string Outcome);

/// <summary>The stats window: totals, the focus chart, focus per preset and a table of days.</summary>
public sealed partial class StatsViewModel(AppViewModel app) : ObservableObject
{
    private const double MaxBarWidth = 320;

    public static readonly IReadOnlyList<StatsRange> Ranges =
    [
        new("Last 7 days", 7),
        new("Last 30 days", 30),
        new("Last 12 months", 365),
    ];

    public AppViewModel App => app;

    public IReadOnlyList<StatsRange> AllRanges => Ranges;

    [ObservableProperty]
    private StatsRange? selectedRange = Ranges[0];

    [ObservableProperty]
    private StatsSnapshot? stats;

    public ObservableCollection<ChartColumn> Columns { get; } = [];

    public ObservableCollection<CategoryBar> Categories { get; } = [];

    public ObservableCollection<DayRow> Rows { get; } = [];

    public string ChartTitle => SelectedRange?.Days > 31 ? "Focus per month" : "Focus per day";

    public string TotalFocus => Stats is null ? "–" : Formatting.Duration(TimeSpan.FromMinutes(Stats.Days.Sum(day => day.FocusedMinutes)));

    public string RangeText => Stats is null ? "" : $"{Stats.From:d MMM yyyy} to {Stats.To:d MMM yyyy}";

    public string Average
    {
        get
        {
            var recorded = Stats?.Days.Where(day => day.Outcome is not null).ToList() ?? [];
            return recorded.Count == 0 ? "–" : Formatting.Duration(TimeSpan.FromMinutes(recorded.Average(day => day.FocusedMinutes)));
        }
    }

    public string OnTarget
    {
        get
        {
            var withMinimum = Stats?.Days.Where(day => day.MinimumMinutes > 0 && day.Outcome is not null and not DayOutcome.Open).ToList() ?? [];
            return withMinimum.Count == 0 ? "–" : $"{withMinimum.Count(day => day.Outcome == DayOutcome.Done)} of {withMinimum.Count}";
        }
    }

    public string Streak => Stats is null ? "–" : Days(Stats.CurrentStreak);

    public string BestStreak => Stats is null ? "–" : Days(Stats.BestStreak);

    public string Interruptions => Stats is null ? "" :
        $"{Stats.Pauses} pause(s), {Stats.Postpones} postpone(s) and {Stats.RecoveryExits} recovery exit(s) in this range.";

    public bool HasCategories => Categories.Count > 0;

    partial void OnSelectedRangeChanged(StatsRange? value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        var response = await app.SendAsync(new StatsRequest(SelectedRange?.Days ?? 7));
        if (response?.Stats is { } snapshot)
            Show(snapshot);
    }

    public void Show(StatsSnapshot snapshot)
    {
        Stats = snapshot;
        var byMonth = snapshot.Days.Count > 31;

        Columns.Clear();
        var weekly = snapshot.Days.Count <= 7;
        foreach (var column in byMonth ? Months(snapshot.Days) : snapshot.Days.Select(day => DayColumn(day, weekly)))
            Columns.Add(column);

        Categories.Clear();
        var largest = Math.Max(1, snapshot.Categories.Select(category => category.FocusedMinutes).DefaultIfEmpty(0).Max());
        foreach (var category in snapshot.Categories)
        {
            Categories.Add(new CategoryBar(
                category.Name,
                $"{Formatting.Duration(TimeSpan.FromMinutes(category.FocusedMinutes))} · {category.Blocks} block(s)",
                Math.Max(4, MaxBarWidth * category.FocusedMinutes / largest)));
        }

        Rows.Clear();
        foreach (var day in snapshot.Days.Where(day => !byMonth || day.Outcome is not null).Reverse())
        {
            Rows.Add(new DayRow(
                $"{day.Day:ddd d MMM}",
                day.Outcome is null ? "–" : Formatting.Duration(TimeSpan.FromMinutes(day.FocusedMinutes)),
                day.GoalMinutes > 0 ? Formatting.Duration(TimeSpan.FromMinutes(day.GoalMinutes)) : "–",
                OutcomeText(day.Outcome)));
        }

        OnPropertyChanged(string.Empty);
    }

    /// <param name="weekly">Label columns by weekday ("Tu") rather than by date ("20").</param>
    private static ChartColumn DayColumn(DayStats day, bool weekly)
    {
        var tooltip = day.Outcome is null
            ? $"{day.Day:dddd d MMM}\nNothing recorded"
            : $"{day.Day:dddd d MMM}\n{Formatting.Duration(TimeSpan.FromMinutes(day.FocusedMinutes))} of focus" +
              (day.GoalMinutes > 0 ? $" (goal {Formatting.Duration(TimeSpan.FromMinutes(day.GoalMinutes))})" : "") +
              $"\n{OutcomeText(day.Outcome)}";
        return new ChartColumn(weekly ? $"{day.Day:ddd}"[..2] : $"{day.Day.Day}", day.FocusedMinutes, day.GoalMinutes, tooltip);
    }

    private static IEnumerable<ChartColumn> Months(IEnumerable<DayStats> days) =>
        days.GroupBy(day => (day.Day.Year, day.Day.Month)).Select(month =>
        {
            var focused = month.Sum(day => day.FocusedMinutes);
            var goal = month.Sum(day => day.GoalMinutes);
            var first = month.First().Day;
            var done = month.Count(day => day.Outcome == DayOutcome.Done);
            return new ChartColumn(
                $"{first:MMM}",
                focused,
                goal,
                $"{first:MMMM yyyy}\n{Formatting.Duration(TimeSpan.FromMinutes(focused))} of focus (goals {Formatting.Duration(TimeSpan.FromMinutes(goal))})\n{done} day(s) on target");
        });

    private static string OutcomeText(DayOutcome? outcome) => outcome switch
    {
        DayOutcome.Done => "✓ On target",
        DayOutcome.Missed => "✕ Missed",
        DayOutcome.Failed => "✕ Recovery exit",
        DayOutcome.Open => "… In progress",
        DayOutcome.NoMinimum => "○ Free day",
        _ => "– Nothing recorded",
    };

    private static string Days(int count) => count == 1 ? "1 day" : $"{count} days";
}
