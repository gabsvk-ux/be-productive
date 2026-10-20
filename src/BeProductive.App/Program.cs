using Avalonia;

namespace BeProductive.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // One copy per user session: a second launch just exits.
        using var instance = new Mutex(initiallyOwned: true, @"Local\BeProductive.App", out var isFirst);
        if (!isFirst)
            return 0;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
