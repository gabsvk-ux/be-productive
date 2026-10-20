using System.Collections.ObjectModel;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeProductive.App.ViewModels;

/// <summary>The small everyday window: the timer, progress and whatever can be done right now.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly AppViewModel app;

    public DashboardViewModel(AppViewModel app)
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

    public ObservableCollection<string> Folders { get; } = [];

    [ObservableProperty]
    private BlockChoice? nextBlock = BlockChoice.For(BlockKind.Classic25);

    [ObservableProperty]
    private Preset? switchTo;

    [ObservableProperty]
    private decimal? extraMinutes = 30;

    [ObservableProperty]
    private bool showExit;

    [ObservableProperty]
    private string exitCode = "";

    private SessionStatus? Status => app.Status;

    private Phase? Phase => Status?.Phase;

    public string Title => Status switch
    {
        null => "Not connected",
        { Phase: Core.Sessions.Phase.Gate } => "Start screen",
        { Phase: Core.Sessions.Phase.Postponed } => "Postponed",
        { Phase: Core.Sessions.Phase.Focus, Block.Paused: true } => "Paused",
        { Phase: Core.Sessions.Phase.Focus, Block.Extra: true } => "Extra focus",
        { Phase: Core.Sessions.Phase.Focus } => "Focus",
        { Phase: Core.Sessions.Phase.Break, LongBreak: true } => "Long break",
        { Phase: Core.Sessions.Phase.Break } => "Break",
        { Phase: Core.Sessions.Phase.AwaitingNext } => "Pick your next block",
        { Outcome: DayOutcome.Done } => "Done for today",
        { Outcome: DayOutcome.Failed } => "Lock ended for today",
        { Outcome: DayOutcome.NoMinimum } => "Free day",
        _ => "Free",
    };

    public string Subtitle => Status switch
    {
        null => app.Error ?? "",
        { Phase: Core.Sessions.Phase.Focus, Block: { } block } => $"{Status.PresetName} · {block.Kind.Label()} block{(block.Note is null ? "" : $" · {block.Note}")}",
        { Phase: Core.Sessions.Phase.Break or Core.Sessions.Phase.AwaitingNext } => $"Next up: {Status.PresetName}",
        { Phase: Core.Sessions.Phase.Postponed } => $"The start screen comes back at {Status.PostponedUntil?.ToLocalTime():HH:mm}.",
        { Outcome: DayOutcome.Done } => "Nice work. Add extra time below if you want to keep going.",
        { Outcome: DayOutcome.Failed } => "A recovery code ended today's lock.",
        _ => "",
    };

    public bool HasTimer => Phase is Core.Sessions.Phase.Focus or Core.Sessions.Phase.Break or Core.Sessions.Phase.Postponed;

    public string TimerText => Status switch
    {
        { Phase: Core.Sessions.Phase.Focus, Block: { } block } => Formatting.Clock(block.Remaining),
        { Phase: Core.Sessions.Phase.Break, BreakEndsAt: { } ends } => Formatting.Clock(ends - app.Now),
        { Phase: Core.Sessions.Phase.Postponed, PostponedUntil: { } until } => Formatting.Clock(until - app.Now),
        _ => "",
    };

    public string TimerCaption => Status switch
    {
        { Phase: Core.Sessions.Phase.Focus, Block.Paused: true, Block.AutoResumeAt: { } resume } => $"Paused. Carries on by itself at {resume.ToLocalTime():HH:mm}.",
        { Phase: Core.Sessions.Phase.Focus } => "left in this block",
        { Phase: Core.Sessions.Phase.Break } => "until the break ends",
        { Phase: Core.Sessions.Phase.Postponed } => "until the start screen",
        _ => "",
    };

    public double GoalMinutes => Status is null ? 0 : Math.Max(Status.BindingMinutes, Status.MinimumMinutes) + Status.ExtraMinutes;

    public double FocusedMinutes => Status?.FocusedToday.TotalMinutes ?? 0;

    public bool HasProgress => GoalMinutes > 0;

    public string ProgressText => Status is null
        ? ""
        : $"{Formatting.Duration(Status.FocusedToday)} of {Formatting.Duration(TimeSpan.FromMinutes(GoalMinutes))} focus today · {Status.CompletedBlocks} block(s)";

    public string LockText => Status switch
    {
        null => "",
        { Restriction: Restriction.Focus } => $"Only {Status.PresetName}'s apps and sites{AdminSuffix}",
        { Restriction: Restriction.Break } => $"Break: most things allowed{AdminSuffix}",
        { Restriction: Restriction.Gate } => $"Locked to the start screen{AdminSuffix}",
        _ => "Nothing is locked",
    };

    private string AdminSuffix => Status?.AdminToolsBlocked == true ? " · admin tools blocked" : "";

    public bool CanPause => Status is { Phase: Core.Sessions.Phase.Focus, Block.Paused: false };

    public bool CanResume => Status is { Phase: Core.Sessions.Phase.Focus, Block.Paused: true };

    public bool IsBreak => Phase is Core.Sessions.Phase.Break;

    public bool CanExtend => Phase is Core.Sessions.Phase.Free && Presets.Count > 0;

    public bool CanStopExtra => Status is { Outcome: not DayOutcome.Open, Phase: Core.Sessions.Phase.Focus or Core.Sessions.Phase.Break or Core.Sessions.Phase.AwaitingNext };

    public bool CanUseRecoveryCode => Status?.AdminToolsBlocked == true;

    public bool HasFolders => Folders.Count > 0 && Phase is Core.Sessions.Phase.Focus or Core.Sessions.Phase.Break;

    [RelayCommand]
    private Task PauseAsync() => app.SendAsync(new PauseRequest());

    [RelayCommand]
    private Task ResumeAsync() => app.SendAsync(new ResumeRequest());

    [RelayCommand]
    private Task StartNextAsync() => app.SendAsync(new StartNextRequest(NextBlock?.Kind ?? BlockKind.Classic25, null));

    [RelayCommand]
    private async Task SwitchPresetAsync()
    {
        if (SwitchTo is not null)
            await app.SendAsync(new SwitchPresetRequest(SwitchTo.Id));
    }

    [RelayCommand]
    private Task ExtendAsync() =>
        app.SendAsync(new ExtendRequest((int)(ExtraMinutes ?? 30), NextBlock?.Kind ?? BlockKind.Classic25, SwitchTo?.Id ?? Status?.PresetId ?? Presets.FirstOrDefault()?.Id, null));

    [RelayCommand]
    private Task StopExtraAsync() => app.SendAsync(new StopExtraRequest());

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
        RefreshFolders();
        OnPropertyChanged(string.Empty);
    }

    private void RefreshFolders()
    {
        var folders = app.CurrentPreset?.FolderShortcuts ?? [];
        if (folders.SequenceEqual(Folders))
            return;
        Folders.Clear();
        foreach (var folder in folders)
            Folders.Add(folder);
    }

    private void RefreshPresets()
    {
        var selectedId = SwitchTo?.Id ?? Status?.PresetId;
        Presets.Clear();
        foreach (var preset in app.Presets)
            Presets.Add(preset);
        SwitchTo = Presets.FirstOrDefault(preset => preset.Id == selectedId) ?? Presets.FirstOrDefault();
        Refresh();
    }
}
