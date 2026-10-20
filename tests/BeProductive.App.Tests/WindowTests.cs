using Avalonia.Headless.XUnit;
using BeProductive.App.ViewModels;
using BeProductive.App.Views;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;

namespace BeProductive.App.Tests;

public class WindowTests
{
    private readonly FakeServiceClient service = new();

    private async Task<AppViewModel> ConnectAsync(SessionStatus status)
    {
        service.Status = status;
        var app = new AppViewModel(service, new FixedTime(FakeServiceClient.Now));
        await app.RefreshAsync();
        return app;
    }

    [AvaloniaFact]
    public async Task The_gate_plans_the_day_with_the_chosen_preset_and_length()
    {
        var app = await ConnectAsync(Statuses.Gate());
        var window = new GateWindow { DataContext = app.Gate };
        Screenshots.Save(window, "gate");

        Assert.Equal("uni", app.Gate.SelectedPreset!.Id);
        app.Gate.SelectedPreset = app.Gate.Presets.Single(preset => preset.Id == "programming");
        Assert.Equal(BlockKind.Long45, app.Gate.SelectedBlock!.Kind);
        app.Gate.PlannedMinutes = 120;
        app.Gate.Note = "Parser";

        await app.Gate.StartCommand.ExecuteAsync(null);

        Assert.Equal(new PlanRequest("programming", 120, BlockKind.Long45, "Parser"), Assert.Single(service.Sent));
    }

    [AvaloniaFact]
    public async Task The_gate_never_plans_less_than_the_minimum()
    {
        var app = await ConnectAsync(Statuses.Gate());

        app.Gate.PlannedMinutesValue = 30;
        app.Gate.AddMinutesCommand.Execute(-60);

        Assert.Equal(100, app.Gate.PlannedMinutes);
        Assert.Equal([100, 120, 180, 240], app.Gate.QuickMinutes);
    }

    [AvaloniaFact]
    public async Task The_gate_cannot_be_closed_by_the_user()
    {
        var app = await ConnectAsync(Statuses.Gate());
        var window = new GateWindow { DataContext = app.Gate };
        window.Show();

        window.Close();
        Assert.True(window.IsVisible);

        window.AllowClose = true;
        window.Close();
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Refusals_from_the_service_are_shown()
    {
        var app = await ConnectAsync(Statuses.Gate());
        service.Refuse = "No postpones left this week.";
        app.Gate.TogglePostponeCommand.Execute(null);

        await app.Gate.PostponeCommand.ExecuteAsync(null);

        Assert.Equal("No postpones left this week.", app.Error);
        Assert.True(app.Gate.ShowPostpone);
        var window = new GateWindow { DataContext = app.Gate };
        Screenshots.Save(window, "gate-postpone-refused");
    }

    [AvaloniaFact]
    public async Task The_dashboard_shows_the_running_block()
    {
        var app = await ConnectAsync(Statuses.Focus());
        var window = new DashboardWindow { DataContext = app.Dashboard };
        Screenshots.Save(window, "dashboard-focus", 440, 720);

        Assert.Equal("Focus", app.Dashboard.Title);
        Assert.Equal("12:42", app.Dashboard.TimerText);
        Assert.True(app.Dashboard.CanPause);
        Assert.False(app.Dashboard.CanStopExtra);
        Assert.True(app.Dashboard.HasFolders);
        Assert.Contains("admin tools blocked", app.Dashboard.LockText);

        await app.Dashboard.PauseCommand.ExecuteAsync(null);
        Assert.IsType<PauseRequest>(Assert.Single(service.Sent));
    }

    [AvaloniaFact]
    public async Task The_dashboard_during_a_break_offers_the_next_block_and_preset_switch()
    {
        var app = await ConnectAsync(Statuses.Break());
        Screenshots.Save(new DashboardWindow { DataContext = app.Dashboard }, "dashboard-break", 440, 720);

        Assert.Equal("Break", app.Dashboard.Title);
        Assert.Equal("04:00", app.Dashboard.TimerText);
        Assert.True(app.Dashboard.IsBreak);
        Assert.False(app.Dashboard.CanPause);
    }

    [AvaloniaFact]
    public async Task The_dashboard_offers_extra_time_once_done()
    {
        var app = await ConnectAsync(Statuses.Done());
        Screenshots.Save(new DashboardWindow { DataContext = app.Dashboard }, "dashboard-done", 440, 720);

        Assert.Equal("Done for today", app.Dashboard.Title);
        Assert.True(app.Dashboard.CanExtend);
        Assert.False(app.Dashboard.CanUseRecoveryCode);

        await app.Dashboard.ExtendCommand.ExecuteAsync(null);
        Assert.Equal(new ExtendRequest(30, BlockKind.Classic25, "uni", null), Assert.Single(service.Sent));
    }

    [AvaloniaFact]
    public async Task The_next_block_prompt_starts_the_picked_block_after_switching_preset()
    {
        var app = await ConnectAsync(Statuses.AwaitingNext());
        Screenshots.Save(new NextBlockWindow { DataContext = app.NextBlock }, "next-block");

        Assert.Contains("42 s", app.NextBlock.Countdown);
        app.NextBlock.SelectedPreset = app.NextBlock.Presets.Single(preset => preset.Id == "projects");

        await app.NextBlock.StartCommand.ExecuteAsync(BlockChoice.For(BlockKind.Long45));

        Assert.Equal(
            [new SwitchPresetRequest("projects"), new StartNextRequest(BlockKind.Long45, null)],
            service.Sent);
    }

    [AvaloniaFact]
    public async Task Settings_show_pending_changes_and_save_presets()
    {
        var tomorrow = new DateOnly(2026, 10, 21);
        var target = new Preset { Id = "uni", Name = "Uni studying", AllowedApps = ["Steam.exe"] };
        service.Pending.Add(new PendingRuleChange(PendingChangeKinds.Preset, "uni", tomorrow, "allow Steam.exe", target, null));
        var app = await ConnectAsync(Statuses.Focus());
        var settings = new SettingsViewModel(app);
        await settings.LoadAsync();
        Screenshots.Save(new SettingsWindow { DataContext = settings }, "settings", 860, 680);

        var uni = settings.Presets.Single(editor => editor.Id == "uni");
        Assert.Equal("From Wed 21 Oct: allow Steam.exe.", uni.PendingText);
        Assert.Equal("Steam.exe", uni.AllowedApps);

        settings.Selected = settings.Presets.Single(editor => editor.Id == "programming");
        settings.Selected.AllowElevatedTerminal = true;
        await settings.SavePresetCommand.ExecuteAsync(null);

        var saved = Assert.IsType<SavePresetRequest>(Assert.Single(service.Sent)).Preset;
        Assert.True(saved.AllowTerminal);
        Assert.True(saved.AllowElevatedTerminal);
        Assert.Equal("Saved.", app.Message);
    }

    [AvaloniaFact]
    public async Task Losing_the_service_is_reported()
    {
        var app = new AppViewModel(new UnreachableService(), new FixedTime(FakeServiceClient.Now));

        await app.RefreshAsync();

        Assert.False(app.Connected);
        Assert.Equal("Not connected", app.Dashboard.Title);
        Assert.Contains("isn't running", app.Error);
    }

    private sealed class UnreachableService : Services.IServiceClient
    {
        public Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<IpcResponse>(new ServiceUnavailableException("The Be Productive service isn't running."));
    }
}
