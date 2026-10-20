namespace BeProductive.Core.SafetyNet;

public enum PolicyValueKind
{
    DWord,
    String,

    /// <summary>A kind we never write and so can't put back faithfully.</summary>
    Unsupported,
}

/// <param name="Data">DWord values as a decimal string, strings as they are.</param>
public sealed record RegistryValueSnapshot(bool Exists, PolicyValueKind? Kind, string? Data)
{
    public static readonly RegistryValueSnapshot Absent = new(false, null, null);
}

/// <summary>
/// The registry, addressed by full key paths such as <c>HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google\Chrome</c>.
/// Only the full hive names are used, because restore.ps1 reads the same paths from the journal.
/// </summary>
public interface IRegistry
{
    RegistryValueSnapshot ReadValue(string keyPath, string name);

    void WriteValue(string keyPath, string name, PolicyValueKind kind, string data);

    void DeleteValue(string keyPath, string name);

    bool KeyExists(string keyPath);

    void CreateKey(string keyPath);

    void DeleteKeyIfEmpty(string keyPath);
}

public interface IHostsFile
{
    string Read();

    void Write(string content);
}

public interface IDnsCache
{
    void Flush();
}

/// <summary>Stands in on systems without an implementation yet, so dry runs still work there.</summary>
public sealed class UnsupportedPlatform : IRegistry, IHostsFile, IDnsCache
{
    public RegistryValueSnapshot ReadValue(string keyPath, string name) => throw NotSupported();

    public void WriteValue(string keyPath, string name, PolicyValueKind kind, string data) => throw NotSupported();

    public void DeleteValue(string keyPath, string name) => throw NotSupported();

    public bool KeyExists(string keyPath) => throw NotSupported();

    public void CreateKey(string keyPath) => throw NotSupported();

    public void DeleteKeyIfEmpty(string keyPath) => throw NotSupported();

    public string Read() => throw NotSupported();

    public void Write(string content) => throw NotSupported();

    public void Flush() => throw NotSupported();

    private static PlatformNotSupportedException NotSupported() =>
        new("This operating system isn't supported yet.");
}
