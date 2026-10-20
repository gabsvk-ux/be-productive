using System.Collections.ObjectModel;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeProductive.App.ViewModels;

/// <summary>The full-screen prompt when a break is over: pick the next block before the default one starts.</summary>
public sealed partial class NextBlockViewModel : ObservableObject
{
    private readonly AppViewModel app;

    public NextBlockViewModel(AppViewModel app)
    {
        this.app = app;
        app.StatusChanged += (_, _) => Refresh();
        app.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppViewModel.Presets))
                RefreshPresets();
        };
    }

    public AppViewModel App => app;

    public IReadOnlyList<BlockChoice> Blocks => BlockChoice.All;

    public ObservableCollection<Preset> Presets { get; } = [];

    [ObservableProperty]
    private Preset? selectedPreset;

    [ObservableProperty]
    private string? note;

    public string Countdown
    {
        get
        {
            var left = app.Status?.NextBlockDeadline is { } deadline ? deadline - app.Now : TimeSpan.Zero;
            var preset = SelectedPreset ?? app.CurrentPreset;
            var defaultBlock = preset?.DefaultBlock.Label() ?? BlockKind.Classic25.Label();
            return $"A {defaultBlock} block starts by itself in {Math.Max(0, (int)Math.Ceiling(left.TotalSeconds))} s.";
        }
    }

    public string Progress => app.Status is { } status
        ? $"{Formatting.Duration(status.FocusedToday)} of {Formatting.Duration(TimeSpan.FromMinutes(Math.Max(status.BindingMinutes, status.MinimumMinutes) + status.ExtraMinutes))} done today."
        : "";

    [RelayCommand]
    private async Task StartAsync(BlockChoice choice)
    {
        if (SelectedPreset is not null && SelectedPreset.Id != app.Status?.PresetId)
        {
            var switched = await app.SendAsync(new SwitchPresetRequest(SelectedPreset.Id));
            if (switched?.Ok != true)
                return;
        }

        var response = await app.SendAsync(new StartNextRequest(choice.Kind, Note));
        if (response?.Ok == true)
            Note = null;
    }

    private void Refresh()
    {
        if (app.Status?.Phase != Phase.AwaitingNext)
            SelectedPreset = Presets.FirstOrDefault(preset => preset.Id == app.Status?.PresetId) ?? SelectedPreset;
        OnPropertyChanged(nameof(Countdown));
        OnPropertyChanged(nameof(Progress));
    }

    private void RefreshPresets()
    {
        var selectedId = SelectedPreset?.Id ?? app.Status?.PresetId;
        Presets.Clear();
        foreach (var preset in app.Presets)
            Presets.Add(preset);
        SelectedPreset = Presets.FirstOrDefault(preset => preset.Id == selectedId) ?? Presets.FirstOrDefault();
    }
}
