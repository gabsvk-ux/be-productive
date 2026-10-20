using BeProductive.Core.Sessions;

namespace BeProductive.App.ViewModels;

public static class Formatting
{
    /// <summary>"12:05" or "1:02:05".</summary>
    public static string Clock(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }

    /// <summary>"45 min", "2 h" or "2 h 30 min".</summary>
    public static string Duration(TimeSpan time)
    {
        var minutes = (int)Math.Round(time.TotalMinutes);
        if (minutes < 60)
            return $"{minutes} min";
        return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }

    /// <summary>
    /// Roughly how long a plan takes in real time, breaks included: whole blocks of
    /// <paramref name="kind"/>, short breaks between them and a long one after every fourth.
    /// </summary>
    public static TimeSpan EstimateWithBreaks(int plannedMinutes, BlockKind kind, SessionOptions? options = null)
    {
        options ??= new SessionOptions();
        var focus = kind.FocusLength();
        var blocks = (int)Math.Ceiling(plannedMinutes / focus.TotalMinutes);
        if (blocks <= 0)
            return TimeSpan.Zero;

        var breaks = blocks - 1;
        var longBreaks = options.LongBreaks && options.LongBreakEvery > 0 ? breaks / options.LongBreakEvery : 0;
        return focus * blocks + kind.ShortBreakLength() * (breaks - longBreaks) + options.LongBreakLength * longBreaks;
    }

    public static string Greeting(DateTime localNow) => localNow.Hour switch
    {
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening",
    };
}

/// <summary>One of the three pomodoro lengths, for pickers.</summary>
public sealed record BlockChoice(BlockKind Kind)
{
    public static readonly IReadOnlyList<BlockChoice> All = Enum.GetValues<BlockKind>().Select(kind => new BlockChoice(kind)).ToList();

    public string Label => Kind.Label();

    public string Caption => $"{Kind.FocusLength().TotalMinutes:0} min focus, {Kind.ShortBreakLength().TotalMinutes:0} min break";

    public static BlockChoice For(BlockKind kind) => All.First(choice => choice.Kind == kind);
}
