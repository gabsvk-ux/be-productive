namespace BeProductive.Core.Sessions;

/// <summary>The pomodoro lengths on offer: focus minutes / short break minutes.</summary>
public enum BlockKind
{
    Short15,
    Classic25,
    Long45,
}

public static class BlockKinds
{
    public static TimeSpan FocusLength(this BlockKind kind) => kind switch
    {
        BlockKind.Short15 => TimeSpan.FromMinutes(15),
        BlockKind.Classic25 => TimeSpan.FromMinutes(25),
        BlockKind.Long45 => TimeSpan.FromMinutes(45),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static TimeSpan ShortBreakLength(this BlockKind kind) => kind switch
    {
        BlockKind.Short15 => TimeSpan.FromMinutes(5),
        BlockKind.Classic25 => TimeSpan.FromMinutes(5),
        BlockKind.Long45 => TimeSpan.FromMinutes(15),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>"15/5", "25/5" or "45/15".</summary>
    public static string Label(this BlockKind kind) =>
        $"{kind.FocusLength().TotalMinutes}/{kind.ShortBreakLength().TotalMinutes}";

    /// <summary>Accepts the focus minutes ("25") or the label ("25/5").</summary>
    public static bool TryParse(string? text, out BlockKind kind)
    {
        foreach (var candidate in Enum.GetValues<BlockKind>())
        {
            if (text == candidate.Label() || text == candidate.FocusLength().TotalMinutes.ToString())
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}
