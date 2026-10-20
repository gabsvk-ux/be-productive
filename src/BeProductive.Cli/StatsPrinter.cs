using BeProductive.Core.Sessions;

namespace BeProductive.Cli;

internal static class StatsPrinter
{
    public static void Print(StatsSnapshot stats)
    {
        var total = stats.Days.Sum(day => day.FocusedMinutes);
        var withGoal = stats.Days.Where(day => day.MinimumMinutes > 0 && day.Outcome is not null and not DayOutcome.Open).ToList();
        var onTarget = withGoal.Count(day => day.Outcome == DayOutcome.Done);

        Console.WriteLine($"{stats.From:ddd d MMM} to {stats.To:ddd d MMM}");
        Console.WriteLine($"Focus:     {Hours(total)} in total, {Hours(total / Math.Max(1, stats.Days.Count))} a day on average");
        Console.WriteLine($"On target: {onTarget} of {withGoal.Count} day(s) with a minimum");
        Console.WriteLine($"Streak:    {stats.CurrentStreak} day(s) now, best {stats.BestStreak}");
        Console.WriteLine($"Also:      {stats.Pauses} pause(s), {stats.Postpones} postpone(s), {stats.RecoveryExits} recovery exit(s)");

        Console.WriteLine();
        foreach (var day in stats.Days)
        {
            var bar = new string('#', Math.Min(40, day.FocusedMinutes / 10));
            var goal = day.GoalMinutes > 0 ? $"/{day.GoalMinutes}" : "";
            Console.WriteLine($"  {day.Day:ddd dd MMM}  {day.FocusedMinutes,4}{goal,-5} min  {Outcome(day.Outcome),-10} {bar}");
        }

        if (stats.Categories.Count == 0)
            return;
        Console.WriteLine();
        foreach (var category in stats.Categories)
            Console.WriteLine($"  {category.Name,-20} {Hours(category.FocusedMinutes),10}  {category.Blocks} block(s)");
    }

    private static string Outcome(DayOutcome? outcome) => outcome switch
    {
        DayOutcome.Done => "done",
        DayOutcome.Missed => "missed",
        DayOutcome.Failed => "exited",
        DayOutcome.Open => "today",
        DayOutcome.NoMinimum => "free day",
        _ => "-",
    };

    private static string Hours(int minutes) => minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
}
