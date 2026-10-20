using Avalonia.Controls;

namespace BeProductive.App.Views;

/// <summary>
/// A full-screen, always-on-top window that can't be closed by the user (Alt+F4 included).
/// Only the app closes it, once the lock it shows is over.
/// </summary>
public class LockWindow : Window
{
    public LockWindow()
    {
        WindowState = WindowState.FullScreen;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        Opened += (_, _) => CoverScreen();
    }

    public bool AllowClose { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
            e.Cancel = true;
        base.OnClosing(e);
    }

    /// <summary>Also sizes the window to the whole screen, for systems that ignore the full-screen request.</summary>
    private void CoverScreen()
    {
        if (WindowState != WindowState.FullScreen || Screens.ScreenFromWindow(this) is not { } screen)
            return;
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;
    }
}
