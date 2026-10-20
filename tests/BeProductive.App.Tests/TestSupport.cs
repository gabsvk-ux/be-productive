using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using BeProductive.App.Services;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;

[assembly: AvaloniaTestApplication(typeof(BeProductive.App.Tests.TestAppBuilder))]

namespace BeProductive.App.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering (not the headless stub) so screenshots show what the app looks like.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

/// <summary>Stands in for the service: answers with a fixed status and records every command.</summary>
internal sealed class FakeServiceClient : IServiceClient
{
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public SessionStatus Status { get; set; } = Statuses.Gate();

    public List<Preset> Presets { get; } =
    [
        new() { Id = "uni", Name = "Uni studying", DefaultBlock = BlockKind.Classic25, FolderShortcuts = [@"C:\Uni\Calculus"] },
        new() { Id = "projects", Name = "Projects", DefaultBlock = BlockKind.Long45 },
        new() { Id = "programming", Name = "Programming", DefaultBlock = BlockKind.Long45, AllowTerminal = true },
    ];

    public List<PendingRuleChange> Pending { get; } = [];

    public List<IpcRequest> Sent { get; } = [];

    /// <summary>When set, commands are refused with this error.</summary>
    public string? Refuse { get; set; }

    public Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken = default)
    {
        if (request is not (StatusRequest or PresetsRequest or SettingsRequest))
            Sent.Add(request);
        if (Refuse is not null && request is not (StatusRequest or PresetsRequest))
            return Task.FromResult(new IpcResponse(false, Refuse, Status));

        var settings = new SettingsSnapshot(Presets, new WeekdayMinimums(), Pending);
        return Task.FromResult(request switch
        {
            PresetsRequest => new IpcResponse(true, null, Status, Presets),
            SettingsRequest => new IpcResponse(true, null, Status, Settings: settings),
            SavePresetRequest => new IpcResponse(true, null, Status, Message: "Saved.", Settings: settings),
            _ => new IpcResponse(true, null, Status),
        });
    }
}

internal static class Statuses
{
    private static readonly DateOnly Today = new(2026, 10, 20);
    private static readonly DateTimeOffset LatestStart = new(2026, 10, 20, 20, 0, 0, TimeSpan.Zero);

    public static SessionStatus Gate() => new(
        Today, Phase.Gate, DayOutcome.Open, Restriction.Gate, true, null, null, 100, 0, 0,
        TimeSpan.Zero, 0, null, null, false, null, null, 3, LatestStart);

    public static SessionStatus Focus(bool paused = false) => new(
        Today, Phase.Focus, DayOutcome.Open, Restriction.Focus, true, "uni", "Uni studying", 100, 120, 0,
        TimeSpan.FromMinutes(37), 1,
        new BlockStatus(BlockKind.Classic25, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(13) - TimeSpan.FromSeconds(18), paused,
            paused ? FakeServiceClient.Now.AddMinutes(9) : null, false, "Calculus chapter 4"),
        null, false, null, null, 3, LatestStart);

    public static SessionStatus Break() => new(
        Today, Phase.Break, DayOutcome.Open, Restriction.Break, true, "uni", "Uni studying", 100, 120, 0,
        TimeSpan.FromMinutes(50), 2, null, FakeServiceClient.Now.AddMinutes(4), false, null, null, 3, LatestStart);

    public static SessionStatus AwaitingNext() => new(
        Today, Phase.AwaitingNext, DayOutcome.Open, Restriction.Focus, true, "uni", "Uni studying", 100, 120, 0,
        TimeSpan.FromMinutes(50), 2, null, null, false, FakeServiceClient.Now.AddSeconds(42), null, 3, LatestStart);

    public static SessionStatus Done() => new(
        Today, Phase.Free, DayOutcome.Done, Restriction.None, false, "uni", "Uni studying", 100, 120, 0,
        TimeSpan.FromMinutes(125), 5, null, null, false, null, null, 3, LatestStart);
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal static class Screenshots
{
    /// <summary>Lays the window out at a laptop-ish size, renders it and saves a PNG next to the test output.</summary>
    public static void Save(Window window, string name, double width = 1280, double height = 800)
    {
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered.");
        var directory = Environment.GetEnvironmentVariable("SCREENSHOT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
