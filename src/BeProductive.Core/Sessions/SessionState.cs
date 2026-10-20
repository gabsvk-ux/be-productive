namespace BeProductive.Core.Sessions;

public enum Phase
{
    /// <summary>The start screen: pick a preset and plan the day (or postpone).</summary>
    Gate,

    /// <summary>The start was postponed; the laptop is free until then.</summary>
    Postponed,

    Focus,

    Break,

    /// <summary>A break ended: waiting for the next block's length, then starting the default one.</summary>
    AwaitingNext,

    /// <summary>Nothing is required right now.</summary>
    Free,
}

public enum DayOutcome
{
    /// <summary>The planned (binding) time isn't done yet.</summary>
    Open,

    Done,

    /// <summary>A recovery code ended the lock.</summary>
    Failed,

    /// <summary>The day ended with the planned time not done.</summary>
    Missed,

    /// <summary>The day's minimum is 0.</summary>
    NoMinimum,
}

/// <summary>What the machine should enforce right now. Turned into actual blocking in step 4.</summary>
public enum Restriction
{
    None,

    /// <summary>Only the start screen.</summary>
    Gate,

    /// <summary>Only the preset's apps and sites.</summary>
    Focus,

    /// <summary>Only the hard blocklist.</summary>
    Break,
}

/// <summary>Everything the engine needs to carry on after a restart. Saved as JSON.</summary>
public sealed class SessionState
{
    public int Version { get; set; } = 1;

    public DateOnly Day { get; set; }

    public Phase Phase { get; set; }

    public DayOutcome Outcome { get; set; }

    public int MinimumMinutes { get; set; }

    /// <summary>What was planned at the gate: at least the minimum, and binding.</summary>
    public int BindingMinutes { get; set; }

    /// <summary>Extra time added after the binding time, for the record. It can be stopped any time.</summary>
    public int ExtraMinutes { get; set; }

    /// <summary>Total focus at which the current extra time ends.</summary>
    public TimeSpan ExtraUntil { get; set; }

    /// <summary>Focus from finished blocks today. The running block's focus isn't included until it ends.</summary>
    public TimeSpan Focused { get; set; }

    public int CompletedBlocks { get; set; }

    public string? PresetId { get; set; }

    public DateTimeOffset? PostponedUntil { get; set; }

    public ActiveBlock? Block { get; set; }

    public DateTimeOffset? BreakEndsAt { get; set; }

    public bool LongBreak { get; set; }

    public DateTimeOffset? NextBlockDeadline { get; set; }

    /// <summary>The last time the engine ran. A long gap since means the laptop was asleep or off.</summary>
    public DateTimeOffset LastTick { get; set; }
}

public sealed class ActiveBlock
{
    public BlockKind Kind { get; set; }

    public required string PresetId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public TimeSpan Focused { get; set; }

    public DateTimeOffset? PausedSince { get; set; }

    /// <summary>Extra time: can be stopped any time.</summary>
    public bool Extra { get; set; }

    public string? Note { get; set; }
}
