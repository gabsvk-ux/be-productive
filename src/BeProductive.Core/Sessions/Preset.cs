namespace BeProductive.Core.Sessions;

/// <summary>A kind of work (Uni, Programming…). It's also the tracking category.</summary>
public sealed class Preset
{
    /// <summary>Short and stable, used in commands and stored records: "uni", "programming".</summary>
    public string Id { get; set; } = "";

    public required string Name { get; set; }

    /// <summary>Executable names allowed during focus on top of the always-allowed set, e.g. "Code.exe".</summary>
    public List<string> AllowedApps { get; set; } = [];

    /// <summary>Folders offered as shortcuts on the focus screen.</summary>
    public List<string> FolderShortcuts { get; set; } = [];

    /// <summary>Domains blocked during focus on top of the global distraction list.</summary>
    public List<string> BlockedDomains { get; set; } = [];

    public BlockKind DefaultBlock { get; set; } = BlockKind.Classic25;

    /// <summary>Windows Terminal, PowerShell and cmd may run during focus, but not as administrator.</summary>
    public bool AllowTerminal { get; set; }

    /// <summary>
    /// Terminals may also run as administrator, e.g. to install things while setting up.
    /// An admin terminal can stop the app, so switch this off once setup is done.
    /// </summary>
    public bool AllowElevatedTerminal { get; set; }
}

/// <summary>A loosening change waiting for the next day.</summary>
/// <param name="Kind"><see cref="PendingChangeKinds"/>.</param>
/// <param name="Target">The preset id, or empty for the minimums.</param>
/// <param name="Data">The full new preset or minimums, as JSON.</param>
public sealed record PendingChange(string Kind, string Target, DateOnly EffectiveOn, string Description, string Data);

public static class PendingChangeKinds
{
    public const string Preset = "preset";
    public const string Minimums = "minimums";
}

/// <summary>Presets, daily minimums and the changes to them that wait for the next day.</summary>
public interface IRulesStore
{
    IReadOnlyList<Preset> All();

    Preset? Find(string id);

    void SavePreset(Preset preset);

    void DeletePreset(string id);

    /// <summary>Null until the minimums have been edited once; the configured defaults apply until then.</summary>
    WeekdayMinimums? LoadMinimums();

    void SaveMinimums(WeekdayMinimums minimums);

    IReadOnlyList<PendingChange> PendingChanges();

    /// <summary>Adds the change, replacing any pending change for the same kind and target.</summary>
    void SavePending(PendingChange change);

    void DeletePending(string kind, string target);
}
