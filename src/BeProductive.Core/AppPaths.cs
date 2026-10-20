using Microsoft.Extensions.Configuration;

namespace BeProductive.Core;

/// <summary>Where the app keeps its state. The service, the CLI and restore.ps1 all use the same directory.</summary>
public sealed class AppPaths(string dataDirectory)
{
    public string DataDirectory { get; } = dataDirectory;

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public string RecoveryCodesFile => Path.Combine(DataDirectory, "recovery-codes.json");

    public string JournalFile => Path.Combine(DataDirectory, "journal.json");

    public string PanicFile => Path.Combine(DataDirectory, "PANIC");

    /// <summary>Uses the <c>DataDirectory</c> setting if present, otherwise the default location.</summary>
    public static AppPaths FromConfiguration(IConfiguration configuration) =>
        new(configuration["DataDirectory"] is { Length: > 0 } directory ? directory : DefaultDataDirectory());

    public static string DefaultDataDirectory() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BeProductive")
            : "/var/lib/be-productive";
}
