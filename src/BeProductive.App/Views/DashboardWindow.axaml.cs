using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace BeProductive.App.Views;

public partial class DashboardWindow : Window
{
    public DashboardWindow() => InitializeComponent();

    /// <summary>Asks for the settings window; the coordinator owns it.</summary>
    public event Action? SettingsRequested;

    /// <summary>Asks for the stats window; the coordinator owns it.</summary>
    public event Action? StatsRequested;

    /// <summary>When false, closing only hides the window (the app keeps running in the tray).</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void OpenSettings(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OpenStats(object? sender, RoutedEventArgs e) => StatsRequested?.Invoke();

    private async void OpenFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path } && Directory.Exists(path))
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
