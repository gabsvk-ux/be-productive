namespace BeProductive.Core.Sessions;

public sealed record BlockRecord(
    DateOnly Day,
    string PresetId,
    BlockKind Kind,
    bool Extra,
    string? Note,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    TimeSpan Focused,
    bool Completed);

public sealed record DayRecord(
    DateOnly Day,
    int MinimumMinutes,
    int BindingMinutes,
    int ExtraMinutes,
    TimeSpan Focused,
    int CompletedBlocks,
    DayOutcome Outcome);

public sealed record SessionEvent(DateTimeOffset At, string Kind, string? Detail);

public static class SessionEventKinds
{
    public const string Plan = "plan";
    public const string Postpone = "postpone";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string AutoResume = "auto-resume";
    public const string AutoStart = "auto-start";
    public const string SkipBreak = "skip-break";
    public const string SwitchPreset = "switch-preset";
    public const string BlockClosedAfterSleep = "block-closed-after-sleep";
    public const string BindingDone = "binding-done";
    public const string Extend = "extend";
    public const string StopExtra = "stop-extra";
    public const string RecoveryExit = "recovery-exit";
    public const string DayMissed = "day-missed";
}

/// <summary>Where the engine's state and its tracking records go.</summary>
public interface ISessionStore
{
    SessionState? LoadState();

    void SaveState(SessionState state);

    void RecordBlock(BlockRecord block);

    void SaveDay(DayRecord day);

    void RecordEvent(SessionEvent sessionEvent);

    int CountEvents(string kind, DateTimeOffset since);
}
