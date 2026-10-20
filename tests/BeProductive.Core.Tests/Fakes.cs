using BeProductive.Core;
using BeProductive.Core.Enforcement;
using BeProductive.Core.Recovery;
using BeProductive.Core.SafetyNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.Tests;

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("be-productive-tests-").FullName;

    public AppPaths AppPaths => new(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal sealed class FakeRegistry : IRegistry
{
    private readonly HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase)
    {
        @"HKEY_LOCAL_MACHINE\SOFTWARE",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies",
    };

    public Dictionary<(string Key, string Name), (PolicyValueKind Kind, string Data)> Values { get; } = new();

    public bool FailWrites { get; set; }

    public bool FailDeletes { get; set; }

    public IReadOnlySet<string> Keys => keys;

    public RegistryValueSnapshot ReadValue(string keyPath, string name) =>
        Values.TryGetValue((keyPath, name), out var value)
            ? new RegistryValueSnapshot(true, value.Kind, value.Data)
            : RegistryValueSnapshot.Absent;

    public void WriteValue(string keyPath, string name, PolicyValueKind kind, string data)
    {
        if (FailWrites)
            throw new IOException("Simulated write failure.");
        if (!keys.Contains(keyPath))
            throw new InvalidOperationException($"Key {keyPath} doesn't exist.");
        Values[(keyPath, name)] = (kind, data);
    }

    public void DeleteValue(string keyPath, string name)
    {
        if (FailDeletes)
            throw new IOException("Simulated delete failure.");
        Values.Remove((keyPath, name));
    }

    public bool KeyExists(string keyPath) => keys.Contains(keyPath);

    public void CreateKey(string keyPath)
    {
        var parts = keyPath.Split('\\');
        for (var depth = 2; depth <= parts.Length; depth++)
            keys.Add(string.Join('\\', parts[..depth]));
    }

    public void DeleteKeyIfEmpty(string keyPath)
    {
        var hasValues = Values.Keys.Any(value => string.Equals(value.Key, keyPath, StringComparison.OrdinalIgnoreCase));
        var hasSubKeys = keys.Any(key => key.StartsWith(keyPath + @"\", StringComparison.OrdinalIgnoreCase));
        if (!hasValues && !hasSubKeys)
            keys.Remove(keyPath);
    }
}

internal sealed class FakeHostsFile(string content) : IHostsFile
{
    public string Content { get; set; } = content;

    public int Writes { get; private set; }

    public string Read() => Content;

    public void Write(string content)
    {
        Content = content;
        Writes++;
    }
}

internal sealed class FakeDnsCache : IDnsCache
{
    public int Flushes { get; private set; }

    public void Flush() => Flushes++;
}

internal sealed class FakeDelivery : IRecoveryCodeDelivery
{
    public List<IReadOnlyList<string>> Sent { get; } = [];

    /// <summary>Fail without sending anything.</summary>
    public bool Fail { get; set; }

    /// <summary>Send, then fail anyway, as when the email goes out but the confirmation is lost.</summary>
    public bool FailAfterSending { get; set; }

    public Task DeliverAsync(IReadOnlyList<string> formattedCodes, CancellationToken cancellationToken)
    {
        if (Fail)
            return Task.FromException(new DeliveryNotSentException("Simulated delivery failure."));
        Sent.Add(formattedCodes);
        return FailAfterSending
            ? Task.FromException(new TaskCanceledException("Simulated timeout."))
            : Task.CompletedTask;
    }
}

internal static class Gates
{
    public static EnforcementGate Create(AppPaths paths, bool dryRun = false, bool panicFileEnabled = true) =>
        new(Options.Create(new EnforcementOptions { DryRun = dryRun, PanicFileEnabled = panicFileEnabled }), paths, NullLogger<EnforcementGate>.Instance);
}
