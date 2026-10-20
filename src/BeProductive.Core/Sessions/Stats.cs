namespace BeProductive.Core.Sessions;

/// <param name="GoalMinutes">What the day asked for: the planned time (at least the minimum) plus any extra time.</param>
/// <param name="Outcome">Null when nothing was recorded that day (the laptop was off, or the service wasn't running).</param>
public sealed record DayStats(DateOnly Day, int MinimumMinutes, int GoalMinutes, int FocusedMinutes, int CompletedBlocks, DayOutcome? Outcome);

public sealed record CategoryStats(string PresetId, string Name, int FocusedMinutes, int Blocks);

/// <summary>
/// Tracking for a range of days.
/// A streak counts days that met their target (or had no minimum) in a row. Days without a record are
/// skipped rather than breaking it, and today only counts once it's done.
/// </summary>
public sealed record StatsSnapshot(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DayStats> Days,
    IReadOnlyList<CategoryStats> Categories,
    int CurrentStreak,
    int BestStreak,
    int Pauses,
    int Postpones,
    int RecoveryExits);

public static class Streaks
{
    /// <summary>The current and best streaks from every recorded day's outcome, oldest first.</summary>
    public static (int Current, int Best) From(IEnumerable<(DateOnly Day, DayOutcome Outcome)> days, DateOnly today)
    {
        var best = 0;
        var run = 0;
        foreach (var (day, outcome) in days.OrderBy(entry => entry.Day))
        {
            if (outcome is DayOutcome.Done or DayOutcome.NoMinimum)
            {
                run++;
                best = Math.Max(best, run);
            }
            else if (day == today && outcome == DayOutcome.Open)
            {
                // Today is still in progress: it neither extends nor breaks the streak.
            }
            else
            {
                run = 0;
            }
        }

        return (run, best);
    }
}
