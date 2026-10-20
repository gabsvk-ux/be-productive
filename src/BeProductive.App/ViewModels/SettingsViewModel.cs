using System.Collections.ObjectModel;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeProductive.App.ViewModels;

/// <summary>
/// Editing presets and daily minimums. Stricter changes apply at once; looser ones wait for the
/// next day, and the editor shows each preset as it will be, with what's still pending.
/// </summary>
public sealed partial class SettingsViewModel(AppViewModel app) : ObservableObject
{
    public AppViewModel App => app;

    public ObservableCollection<PresetEditor> Presets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PresetEditor? selected;

    [ObservableProperty]
    private MinimumsEditor minimums = new(new WeekdayMinimums(), null);

    public bool HasSelection => Selected is not null;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var response = await app.SendAsync(new SettingsRequest());
        if (response?.Settings is { } settings)
            Load(settings);
    }

    [RelayCommand]
    private void NewPreset()
    {
        var editor = PresetEditor.New();
        Presets.Add(editor);
        Selected = editor;
    }

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        if (Selected is null)
            return;
        var selectedId = Selected.Id;
        var response = await app.SendAsync(new SavePresetRequest(Selected.ToPreset()));
        if (response?.Settings is { } settings)
        {
            Load(settings, selectedId.Length > 0 ? selectedId : null);
            app.InvalidatePresets();
        }
    }

    [RelayCommand]
    private async Task DeletePresetAsync()
    {
        if (Selected is null)
            return;
        if (Selected.IsNew && Selected.Id.Length == 0)
        {
            Presets.Remove(Selected);
            Selected = Presets.FirstOrDefault();
            return;
        }

        var response = await app.SendAsync(new DeletePresetRequest(Selected.Id));
        if (response?.Settings is { } settings)
        {
            Load(settings);
            app.InvalidatePresets();
        }
    }

    [RelayCommand]
    private async Task SaveMinimumsAsync()
    {
        var response = await app.SendAsync(new SaveMinimumsRequest(Minimums.ToMinimums()));
        if (response?.Settings is { } settings)
            Load(settings, Selected?.Id);
    }

    public void Load(SettingsSnapshot settings, string? selectId = null)
    {
        selectId ??= Selected?.Id;
        var pendingPresets = settings.Pending.Where(change => change.Preset is not null).ToDictionary(change => change.Target);

        Presets.Clear();
        foreach (var preset in settings.Presets)
        {
            pendingPresets.Remove(preset.Id, out var pending);
            Presets.Add(PresetEditor.From(pending?.Preset ?? preset, pending, isNew: false));
        }

        // New presets that only exist from tomorrow.
        foreach (var pending in pendingPresets.Values)
            Presets.Add(PresetEditor.From(pending.Preset!, pending, isNew: true));

        Selected = Presets.FirstOrDefault(editor => editor.Id == selectId) ?? Presets.FirstOrDefault();

        var pendingMinimums = settings.Pending.FirstOrDefault(change => change.Minimums is not null);
        Minimums = new MinimumsEditor(pendingMinimums?.Minimums ?? settings.Minimums, pendingMinimums);
    }
}

public sealed partial class PresetEditor : ObservableObject
{
    public IReadOnlyList<BlockChoice> Blocks => BlockChoice.All;

    public string Id { get; private init; } = "";

    public bool IsNew { get; private init; }

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private BlockChoice? defaultBlock = BlockChoice.For(BlockKind.Classic25);

    [ObservableProperty]
    private string allowedApps = "";

    [ObservableProperty]
    private string blockedDomains = "";

    [ObservableProperty]
    private string folders = "";

    [ObservableProperty]
    private bool allowTerminal;

    [ObservableProperty]
    private bool allowElevatedTerminal;

    /// <summary>Changes that only apply from the next day, or null.</summary>
    public string? PendingText { get; private init; }

    public bool HasPending => PendingText is not null;

    public string ListLabel => IsNew ? $"{Name} (from tomorrow)" : HasPending ? $"{Name} •" : Name;

    partial void OnAllowElevatedTerminalChanged(bool value)
    {
        if (value)
            AllowTerminal = true;
    }

    partial void OnAllowTerminalChanged(bool value)
    {
        if (!value)
            AllowElevatedTerminal = false;
    }

    public static PresetEditor New() => new() { Name = "New preset", IsNew = true };

    public static PresetEditor From(Preset preset, PendingRuleChange? pending, bool isNew) => new()
    {
        Id = preset.Id,
        IsNew = isNew,
        Name = preset.Name,
        DefaultBlock = BlockChoice.For(preset.DefaultBlock),
        AllowedApps = string.Join(Environment.NewLine, preset.AllowedApps),
        BlockedDomains = string.Join(Environment.NewLine, preset.BlockedDomains),
        Folders = string.Join(Environment.NewLine, preset.FolderShortcuts),
        AllowTerminal = preset.AllowTerminal,
        AllowElevatedTerminal = preset.AllowElevatedTerminal,
        PendingText = pending is null ? null : $"From {pending.EffectiveOn:ddd d MMM}: {pending.Description}.",
    };

    public Preset ToPreset() => new()
    {
        Id = Id,
        Name = Name,
        DefaultBlock = DefaultBlock?.Kind ?? BlockKind.Classic25,
        AllowedApps = Lines(AllowedApps),
        BlockedDomains = Lines(BlockedDomains),
        FolderShortcuts = Lines(Folders),
        AllowTerminal = AllowTerminal,
        AllowElevatedTerminal = AllowElevatedTerminal,
    };

    private static List<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}

public sealed partial class MinimumsEditor(WeekdayMinimums minimums, PendingRuleChange? pending) : ObservableObject
{
    [ObservableProperty]
    private decimal? monday = minimums.Monday;

    [ObservableProperty]
    private decimal? tuesday = minimums.Tuesday;

    [ObservableProperty]
    private decimal? wednesday = minimums.Wednesday;

    [ObservableProperty]
    private decimal? thursday = minimums.Thursday;

    [ObservableProperty]
    private decimal? friday = minimums.Friday;

    [ObservableProperty]
    private decimal? saturday = minimums.Saturday;

    [ObservableProperty]
    private decimal? sunday = minimums.Sunday;

    public string? PendingText { get; } = pending is null ? null : $"From {pending.EffectiveOn:ddd d MMM}: {pending.Description}.";

    public bool HasPending => PendingText is not null;

    public WeekdayMinimums ToMinimums() => new()
    {
        Monday = (int)(Monday ?? 0),
        Tuesday = (int)(Tuesday ?? 0),
        Wednesday = (int)(Wednesday ?? 0),
        Thursday = (int)(Thursday ?? 0),
        Friday = (int)(Friday ?? 0),
        Saturday = (int)(Saturday ?? 0),
        Sunday = (int)(Sunday ?? 0),
    };
}
