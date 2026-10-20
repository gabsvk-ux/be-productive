namespace BeProductive.Core.Sessions;

public sealed class SessionOptions
{
    public const string SectionName = "Sessions";

    /// <summary>When a new day begins. A 01:00 session still counts as the day before.</summary>
    public TimeSpan DayStartsAt { get; set; } = TimeSpan.FromHours(4);

    /// <summary>The latest time the start can be postponed to.</summary>
    public TimeSpan LatestStart { get; set; } = TimeSpan.FromHours(20);

    /// <summary>Postpones allowed per week (the week starts Monday at <see cref="DayStartsAt"/>).</summary>
    public int PostponesPerWeek { get; set; } = 3;

    public bool LongBreaks { get; set; } = true;

    /// <summary>A long break replaces the short one after every this-many completed blocks.</summary>
    public int LongBreakEvery { get; set; } = 4;

    public TimeSpan LongBreakLength { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>A pause ends on its own after this long.</summary>
    public TimeSpan MaxPause { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the "pick the next block" prompt waits before starting the preset's default block.</summary>
    public TimeSpan NextBlockDeadline { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>A block resumes after sleep or shutdown shorter than this; after a longer one it's closed.</summary>
    public TimeSpan SleepResumeLimit { get; set; } = TimeSpan.FromMinutes(30);

    public WeekdayMinimums MinimumMinutes { get; set; } = new();
}

/// <summary>Minimum focus minutes per weekday. 0 means no gate that day.</summary>
public sealed class WeekdayMinimums
{
    public int Monday { get; set; } = 100;

    public int Tuesday { get; set; } = 100;

    public int Wednesday { get; set; } = 100;

    public int Thursday { get; set; } = 100;

    public int Friday { get; set; } = 100;

    public int Saturday { get; set; } = 100;

    public int Sunday { get; set; } = 100;

    public int For(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, null),
    };
}
