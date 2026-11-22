using BeProductive.Core;
using BeProductive.Core.Sessions;

namespace BeProductive.Service;

/// <summary>
/// Records a tamper attempt whenever settings.json is edited while the service runs. It doesn't act on the
/// change (the config isn't reloaded live, and an armed install ignores anything that would weaken the lock)
/// — it just leaves a trace in the stats so edits to the config are visible.
/// </summary>
public sealed class ConfigWatchWorker(AppPaths paths, ISessionStore store, TimeProvider time, ILogger<ConfigWatchWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);
    private DateTimeOffset lastLogged = DateTimeOffset.MinValue;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = Path.GetDirectoryName(paths.SettingsFile);
        var file = Path.GetFileName(paths.SettingsFile);
        if (string.IsNullOrEmpty(directory))
            return Task.CompletedTask;

        try
        {
            Directory.CreateDirectory(directory);
            var watcher = new FileSystemWatcher(directory, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Renamed += OnChanged;
            stoppingToken.Register(() =>
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't watch the config file for changes");
        }

        return Task.CompletedTask;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var now = time.GetUtcNow();
        if (now - lastLogged < Debounce)
            return;
        lastLogged = now;

        try
        {
            logger.LogWarning("settings.json was changed while the service was running (recorded as a tamper attempt)");
            store.RecordEvent(new SessionEvent(now, SessionEventKinds.TamperAttempt, "config-edited"));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't record a config-change tamper attempt");
        }
    }
}
