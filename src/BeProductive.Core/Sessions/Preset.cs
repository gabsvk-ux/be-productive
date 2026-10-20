namespace BeProductive.Core.Sessions;

/// <summary>A kind of work (Uni, Programming…). It's also the tracking category.</summary>
public sealed class Preset
{
    /// <summary>Short and stable, used in commands and stored records: "uni", "programming".</summary>
    public required string Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Executable names allowed during focus on top of the always-allowed set, e.g. "Code.exe".</summary>
    public List<string> AllowedApps { get; set; } = [];

    /// <summary>Folders offered as shortcuts on the focus screen.</summary>
    public List<string> FolderShortcuts { get; set; } = [];

    /// <summary>Domains blocked during focus on top of the global distraction list.</summary>
    public List<string> BlockedDomains { get; set; } = [];

    public BlockKind DefaultBlock { get; set; } = BlockKind.Classic25;
}

public interface IPresetStore
{
    IReadOnlyList<Preset> All();

    Preset? Find(string id);
}
