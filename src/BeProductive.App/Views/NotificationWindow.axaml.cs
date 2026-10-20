using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace BeProductive.App.Views;

/// <summary>A small note in the corner of the screen that goes away by itself (or when clicked).</summary>
public partial class NotificationWindow : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(8);

    public NotificationWindow() => InitializeComponent();

    public static void Show(string heading, string body)
    {
        var window = new NotificationWindow();
        window.Heading.Text = heading;
        window.Body.Text = body;
        window.PointerPressed += (_, _) => window.Close();
        window.Opened += (_, _) => window.MoveToCorner();
        window.Show();
        DispatcherTimer.RunOnce(window.Close, Lifetime);
    }

    private void MoveToCorner()
    {
        if (Screens.Primary is not { } screen)
            return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        Position = new PixelPoint(
            area.Right - (int)(Bounds.Width * scale) - 16,
            area.Bottom - (int)(Bounds.Height * scale) - 16);
    }
}
