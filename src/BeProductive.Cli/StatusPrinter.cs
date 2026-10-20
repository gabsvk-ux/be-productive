using BeProductive.Core.Sessions;

namespace BeProductive.Cli;

internal static class StatusPrinter
{
    public static void Print(SessionStatus status)
    {
        Console.WriteLine($"Today:        {status.Day:ddd d MMM}, {Describe(status)}");
        if (status.Outcome is not DayOutcome.NoMinimum)
        {
            var goal = status.BindingMinutes > 0
                ? $"{status.BindingMinutes} min planned (minimum {status.MinimumMinutes})"
                : $"minimum {status.MinimumMinutes} min";
            var extra = status.ExtraMinutes > 0 ? $", +{status.ExtraMinutes} min extra" : "";
            Console.WriteLine($"Focus:        {Minutes(status.FocusedToday)} of {goal}{extra}, {status.CompletedBlocks} block(s) done");
        }

        Console.WriteLine($"Locked:       {LockDescription(status)}");
        if (status.Phase == Phase.Gate)
            Console.WriteLine($"Postpones:    {status.PostponesLeft} left this week (latest start {status.LatestStart.ToLocalTime():HH:mm})");
    }

    private static string Describe(SessionStatus status)
    {
        var preset = status.PresetName ?? status.PresetId;
        return status.Phase switch
        {
            Phase.Gate => "waiting at the start screen: run `bp plan` (or `bp postpone`)",
            Phase.Postponed => $"postponed until {status.PostponedUntil?.ToLocalTime():HH:mm}",
            Phase.Focus when status.Block is { } block => Block(preset, block),
            Phase.Break => $"{(status.LongBreak ? "long break" : "break")} until {status.BreakEndsAt?.ToLocalTime():HH:mm:ss}",
            Phase.AwaitingNext => $"break over: run `bp next` (the default block starts at {status.NextBlockDeadline?.ToLocalTime():HH:mm:ss})",
            Phase.Free => status.Outcome switch
            {
                DayOutcome.Done => "done for today",
                DayOutcome.Failed => "lock ended with a recovery code",
                DayOutcome.NoMinimum => "nothing required today",
                _ => "free",
            },
            _ => status.Phase.ToString(),
        };
    }

    private static string Block(string? preset, BlockStatus block)
    {
        var text = $"{preset}, {block.Kind.Label()} block, {Clock(block.Remaining)} left";
        if (block.Extra)
            text += " (extra time)";
        if (block.Paused)
            text += $", paused until {block.AutoResumeAt?.ToLocalTime():HH:mm:ss}";
        if (block.Note is not null)
            text += $": {block.Note}";
        return text;
    }

    private static string LockDescription(SessionStatus status)
    {
        var restriction = status.Restriction switch
        {
            Restriction.Gate => "start screen only",
            Restriction.Focus => "the preset's apps and sites only",
            Restriction.Break => "hard blocklist only",
            _ => "nothing",
        };
        return status.AdminToolsBlocked ? $"{restriction}; admin tools blocked" : restriction;
    }

    private static string Minutes(TimeSpan time) => $"{(int)time.TotalMinutes} min";

    private static string Clock(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";
}
