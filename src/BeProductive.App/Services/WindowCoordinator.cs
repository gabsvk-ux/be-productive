using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BeProductive.App.ViewModels;
using BeProductive.App.Views;
using BeProductive.Core.Sessions;

namespace BeProductive.App.Services;

/// <summary>
/// Decides which windows are on screen from the latest status: the gate while the day isn't planned,
/// the next-block prompt after a break, the dashboard otherwise. Also plays the sounds, shows the
/// notifications and owns the tray icon.
/// </summary>
public sealed class WindowCoordinator(Application application, IClassicDesktopStyleApplicationLifetime desktop, AppViewModel app)
{
    private readonly DashboardWindow dashboard = new() { DataContext = app.Dashboard };
    private GateWindow? gate;
    private NextBlockWindow? nextBlock;
    private SettingsWindow? settings;
    private NativeMenuItem? quitItem;
    private bool refreshing;

    public void Start()
    {
        dashboard.SettingsRequested += ShowSettings;
        app.StatusChanged += OnStatusChanged;
        CreateTrayIcon();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();

        _ = RefreshAsync().ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (gate is null)
                ShowDashboard();
        }));
    }

    private async Task RefreshAsync()
    {
        if (refreshing)
            return;
        refreshing = true;
        try
        {
            await app.RefreshAsync();
        }
        finally
        {
            refreshing = false;
        }
    }

    private void OnStatusChanged(SessionStatus? before, SessionStatus? after)
    {
        UpdateLockWindows(after);
        Announce(before, after);
        if (quitItem is not null)
            quitItem.IsEnabled = after?.AdminToolsBlocked != true;
    }

    private void UpdateLockWindows(SessionStatus? status)
    {
        if (status?.Phase == Phase.Gate)
        {
            if (gate is null)
            {
                gate = new GateWindow { DataContext = app.Gate };
                gate.Show();
            }

            gate.Activate();
        }
        else if (gate is not null)
        {
            gate.AllowClose = true;
            gate.Close();
            gate = null;
            ShowDashboard();
        }

        if (status?.Phase == Phase.AwaitingNext)
        {
            if (nextBlock is null)
            {
                nextBlock = new NextBlockWindow { DataContext = app.NextBlock };
                nextBlock.Show();
                Sound.Alert();
            }
        }
        else if (nextBlock is not null)
        {
            nextBlock.AllowClose = true;
            nextBlock.Close();
            nextBlock = null;
        }
    }

    private static void Announce(SessionStatus? before, SessionStatus? after)
    {
        if (before is null || after is null)
            return;

        if (before.Phase == Phase.Focus && after.Phase == Phase.Break)
        {
            Sound.Chime();
            var length = after.BreakEndsAt is { } ends ? Formatting.Duration(ends - DateTimeOffset.UtcNow) : "";
            NotificationWindow.Show(after.LongBreak ? "Long break" : "Break time", $"{length} off. Stand up, stretch, drink some water.");
        }
        else if (before.Outcome == DayOutcome.Open && after.Outcome == DayOutcome.Done)
        {
            Sound.Chime();
            NotificationWindow.Show("Done for today", $"{Formatting.Duration(after.FocusedToday)} of focus. The laptop is yours.");
        }
        else if (before.Block is { Extra: true } && after.Phase == Phase.Free)
        {
            Sound.Chime();
            NotificationWindow.Show("Extra time done", $"{Formatting.Duration(after.FocusedToday)} of focus today.");
        }
    }

    private void ShowDashboard()
    {
        dashboard.Show();
        dashboard.Activate();
    }

    private void ShowSettings()
    {
        if (settings is null)
        {
            var viewModel = new SettingsViewModel(app);
            settings = new SettingsWindow { DataContext = viewModel };
            settings.Closed += (_, _) => settings = null;
            _ = viewModel.LoadAsync();
        }

        settings.Show();
        settings.Activate();
    }

    private void Quit()
    {
        if (app.Status?.AdminToolsBlocked == true)
            return;
        dashboard.AllowClose = true;
        desktop.Shutdown();
    }

    private void CreateTrayIcon()
    {
        var open = new NativeMenuItem("Open Be Productive");
        open.Click += (_, _) => ShowDashboard();
        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) => ShowSettings();
        quitItem = new NativeMenuItem("Quit (once today's focus is done)");
        quitItem.Click += (_, _) => Quit();

        var tray = new TrayIcon
        {
            Icon = CreateIcon(),
            ToolTipText = "Be Productive",
            Menu = [open, settingsItem, new NativeMenuItemSeparator(), quitItem],
        };
        tray.Clicked += (_, _) => ShowDashboard();
        TrayIcon.SetIcons(application, [tray]);
    }

    /// <summary>A teal rounded square with a white ring: drawn here so there's no icon file to ship.</summary>
    private static WindowIcon CreateIcon()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#14B8A6")), null, new RoundedRect(new Rect(0, 0, 64, 64), 14));
            context.DrawEllipse(null, new Pen(Brushes.White, 6), new Point(32, 34), 17, 17);
            context.DrawLine(new Pen(Brushes.White, 5, lineCap: PenLineCap.Round), new Point(32, 34), new Point(32, 23));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}
