namespace BeProductive.Core.Sessions;

/// <summary>A snapshot of the day for the UI and the CLI.</summary>
public sealed record SessionStatus(
    DateOnly Day,
    Phase Phase,
    DayOutcome Outcome,
    Restriction Restriction,
    bool AdminToolsBlocked,
    string? PresetId,
    string? PresetName,
    int MinimumMinutes,
    int BindingMinutes,
    int ExtraMinutes,
    TimeSpan FocusedToday,
    int CompletedBlocks,
    BlockStatus? Block,
    DateTimeOffset? BreakEndsAt,
    bool LongBreak,
    DateTimeOffset? NextBlockDeadline,
    DateTimeOffset? PostponedUntil,
    int PostponesLeft,
    DateTimeOffset LatestStart);

public sealed record BlockStatus(
    BlockKind Kind,
    TimeSpan Focused,
    TimeSpan Remaining,
    bool Paused,
    DateTimeOffset? AutoResumeAt,
    bool Extra,
    string? Note);
