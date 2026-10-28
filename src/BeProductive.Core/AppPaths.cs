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

    /// <summary>
    /// A second panic file in a place any user can write without administrator rights, so the escape
    /// hatch works even when the data directory is locked down. On Windows that's the shared Public
    /// folder; elsewhere it falls back under the data directory.
    /// </summary>
    public string PublicPanicFile => Path.Combine(PublicDirectory(), "BEPRODUCTIVE-PANIC.txt");

    /// <summary>Every place a panic file is honoured. Any one of them switches enforcement off.</summary>
    public IEnumerable<string> PanicFiles => [PanicFile, PublicPanicFile];

    public string DatabaseFile => Path.Combine(DataDirectory, "be-productive.db");

    /// <summary>Uses the <c>DataDirectory</c> setting if present, otherwise the default location.</summary>
    public static AppPaths FromConfiguration(IConfiguration configuration) =>
        new(configuration["DataDirectory"] is { Length: > 0 } directory ? directory : DefaultDataDirectory());

    public static string DefaultDataDirectory() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BeProductive")
            : "/var/lib/be-productive";

    /// <summary>A world-writable directory: Windows' Public profile, else the data directory as a fallback.</summary>
    private string PublicDirectory()
    {
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("PUBLIC") is { Length: > 0 } common)
            return common;
        return DataDirectory;
    }
}
