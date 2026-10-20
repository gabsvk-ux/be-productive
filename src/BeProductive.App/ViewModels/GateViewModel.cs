using System.Collections.ObjectModel;
using System.Globalization;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeProductive.App.ViewModels;

/// <summary>The start screen: pick a preset, plan the day and start, or postpone.</summary>
public sealed partial class GateViewModel : ObservableObject
{
    private readonly AppViewModel app;

    public GateViewModel(AppViewModel app)
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

    public ObservableCollection<Preset> Presets { get; } = [];

    public IReadOnlyList<BlockChoice> Blocks => BlockChoice.All;

    public ObservableCollection<int> QuickMinutes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Estimate))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private Preset? selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Estimate))]
    private BlockChoice? selectedBlock = BlockChoice.For(BlockKind.Classic25);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Estimate))]
    [NotifyPropertyChangedFor(nameof(PlannedMinutesValue))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private int plannedMinutes = 100;

    /// <summary><see cref="PlannedMinutes"/> for the number box, which works in decimals.</summary>
    public decimal? PlannedMinutesValue
    {
        get => PlannedMinutes;
        set => PlannedMinutes = (int)(value ?? MinimumMinutes);
    }

    public decimal MinimumValue => MinimumMinutes;

    [ObservableProperty]
    private string? note;

    [ObservableProperty]
    private bool showPostpone;

    [ObservableProperty]
    private string postponeTime = "";

    [ObservableProperty]
    private bool showExit;

    [ObservableProperty]
    private string exitCode = "";

    public int MinimumMinutes => app.Status?.MinimumMinutes ?? 0;

    public string Heading => $"{Formatting.Greeting(app.Now.ToLocalTime().DateTime)}. {app.Now.ToLocalTime():dddd d MMMM}.";

    public string Requirement => $"Today needs at least {Formatting.Duration(TimeSpan.FromMinutes(MinimumMinutes))} of focus before the laptop is yours.";

    public string Estimate
    {
        get
        {
            var total = Formatting.EstimateWithBreaks(PlannedMinutes, SelectedBlock?.Kind ?? BlockKind.Classic25);
            var finish = app.Now.ToLocalTime() + total;
            return $"About {Formatting.Duration(total)} with breaks: done around {finish:HH:mm}.";
        }
    }

    public int PostponesLeft => app.Status?.PostponesLeft ?? 0;

    public bool CanPostpone => PostponesLeft > 0;

    public string PostponeCaption => CanPostpone
        ? $"Postpone ({PostponesLeft} left this week, latest start {app.Status?.LatestStart.ToLocalTime():HH:mm})"
        : "No postpones left this week";

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (value is not null)
            SelectedBlock = BlockChoice.For(value.DefaultBlock);
    }

    [RelayCommand]
    private void ChooseMinutes(int minutes) => PlannedMinutes = minutes;

    [RelayCommand]
    private void AddMinutes(int minutes) => PlannedMinutes = Math.Max(MinimumMinutes, PlannedMinutes + minutes);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (SelectedPreset is null)
            return;
        await app.SendAsync(new PlanRequest(SelectedPreset.Id, PlannedMinutes, SelectedBlock?.Kind ?? SelectedPreset.DefaultBlock, Note));
    }

    private bool CanStart() => SelectedPreset is not null && PlannedMinutes >= MinimumMinutes;

    [RelayCommand]
    private void TogglePostpone()
    {
        ShowPostpone = !ShowPostpone;
        if (ShowPostpone && PostponeTime.Length == 0)
        {
            // An hour from now, rounded up to the next half hour.
            var inAnHour = app.Now.ToLocalTime().AddHours(1);
            var rounded = inAnHour.AddMinutes((30 - inAnHour.Minute % 30) % 30);
            PostponeTime = $"{rounded:HH:mm}";
        }
    }

    [RelayCommand]
    private async Task PostponeAsync()
    {
        if (!TimeOnly.TryParseExact(PostponeTime.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            app.Error = "Type the time as HH:mm, for example 16:30.";
            return;
        }

        var today = app.Now.ToLocalTime();
        var local = today.Date + time.ToTimeSpan();
        var until = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        var response = await app.SendAsync(new PostponeRequest(until));
        if (response?.Ok == true)
            ShowPostpone = false;
    }

    [RelayCommand]
    private void ToggleExit() => ShowExit = !ShowExit;

    [RelayCommand]
    private async Task ExitAsync()
    {
        var response = await app.SendAsync(new RecoveryExitRequest(ExitCode));
        if (response?.Ok == true)
        {
            ExitCode = "";
            ShowExit = false;
        }
    }

    private void Refresh()
    {
        if (PlannedMinutes < MinimumMinutes)
            PlannedMinutes = MinimumMinutes;
        RefreshQuickMinutes();
        OnPropertyChanged(nameof(MinimumMinutes));
        OnPropertyChanged(nameof(MinimumValue));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Requirement));
        OnPropertyChanged(nameof(Estimate));
        OnPropertyChanged(nameof(PostponesLeft));
        OnPropertyChanged(nameof(CanPostpone));
        OnPropertyChanged(nameof(PostponeCaption));
        StartCommand.NotifyCanExecuteChanged();
    }

    private void RefreshQuickMinutes()
    {
        var choices = new[] { MinimumMinutes, 120, 180, 240 }.Where(minutes => minutes >= MinimumMinutes && minutes > 0).Distinct().Order().ToList();
        if (choices.SequenceEqual(QuickMinutes))
            return;
        QuickMinutes.Clear();
        foreach (var minutes in choices)
            QuickMinutes.Add(minutes);
    }

    private void RefreshPresets()
    {
        var selectedId = SelectedPreset?.Id;
        Presets.Clear();
        foreach (var preset in app.Presets)
            Presets.Add(preset);
        SelectedPreset = Presets.FirstOrDefault(preset => preset.Id == selectedId) ?? Presets.FirstOrDefault();
    }
}
