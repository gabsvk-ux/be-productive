using BeProductive.App.Services;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BeProductive.App.ViewModels;

/// <summary>
/// The app's connection to the service and the latest status from it. Every screen reads from here
/// and sends its commands through <see cref="SendAsync"/>.
/// </summary>
public sealed partial class AppViewModel : ObservableObject
{
    private static readonly TimeSpan PresetRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceClient client;
    private DateTimeOffset presetsFetchedAt = DateTimeOffset.MinValue;

    public AppViewModel(IServiceClient client, TimeProvider time)
    {
        this.client = client;
        Time = time;
        Gate = new GateViewModel(this);
        Dashboard = new DashboardViewModel(this);
        NextBlock = new NextBlockViewModel(this);
    }

    public TimeProvider Time { get; }

    public GateViewModel Gate { get; }

    public DashboardViewModel Dashboard { get; }

    public NextBlockViewModel NextBlock { get; }

    [ObservableProperty]
    private SessionStatus? status;

    [ObservableProperty]
    private bool connected;

    [ObservableProperty]
    private IReadOnlyList<Preset> presets = [];

    /// <summary>Why the last command was refused, or why the service can't be reached.</summary>
    [ObservableProperty]
    private string? error;

    /// <summary>Anything else the last command had to say.</summary>
    [ObservableProperty]
    private string? message;

    /// <summary>Raised on the UI thread whenever a new status arrives: (before, after).</summary>
    public event Action<SessionStatus?, SessionStatus?>? StatusChanged;

    public Preset? CurrentPreset => Status?.PresetId is { } id ? Presets.FirstOrDefault(preset => preset.Id == id) : null;

    public DateTimeOffset Now => Time.GetUtcNow();

    /// <summary>Fetches the status (and every so often the presets). Call about once a second.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            if (Now - presetsFetchedAt > PresetRefreshInterval || Presets.Count == 0)
            {
                var withPresets = await client.SendAsync(new PresetsRequest());
                Presets = withPresets.Presets ?? [];
                presetsFetchedAt = Now;
                Apply(withPresets.Status);
            }
            else
            {
                Apply((await client.SendAsync(new StatusRequest())).Status);
            }

            if (!Connected)
                Error = null;
            Connected = true;
        }
        catch (Exception exception) when (exception is ServiceUnavailableException or IOException or TimeoutException)
        {
            Connected = false;
            Error = "The Be Productive service isn't running.";
            Apply(null);
        }
    }

    /// <summary>Sends a command. Returns whether the service accepted it; if not, <see cref="Error"/> says why.</summary>
    public async Task<IpcResponse?> SendAsync(IpcRequest request)
    {
        try
        {
            var response = await client.SendAsync(request);
            Error = response.Ok ? null : response.Error;
            Message = response.Message;
            if (response.Presets is not null)
                Presets = response.Presets;
            if (response.Settings is not null)
                Presets = response.Settings.Presets;
            Apply(response.Status);
            return response;
        }
        catch (Exception exception) when (exception is ServiceUnavailableException or IOException or TimeoutException)
        {
            Connected = false;
            Error = "The Be Productive service isn't running.";
            return null;
        }
    }

    /// <summary>Forces the presets to be fetched again on the next refresh.</summary>
    public void InvalidatePresets() => presetsFetchedAt = DateTimeOffset.MinValue;

    private void Apply(SessionStatus? next)
    {
        var previous = Status;
        Status = next;
        OnPropertyChanged(nameof(CurrentPreset));
        StatusChanged?.Invoke(previous, next);
    }
}
