using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Linux;

/// <summary>
/// The Linux stand-in for the registry: managed-policy JSON files, which is how Chrome and Chromium take
/// machine policies on Linux (<c>/etc/opt/chrome/policies/managed/*.json</c>). It speaks the same
/// <see cref="IRegistry"/> key/value shape, so <see cref="SystemChanges"/> journals and undoes these changes
/// exactly as it does registry values, with no change to the journal format.
///
/// Key paths start with <c>FILE\</c> followed by the absolute path with backslashes, for example
/// <c>FILE\etc\opt\chrome\policies\managed\be-productive.json</c>. A key whose last part ends in ".json" is a
/// policy file (its values are the top-level JSON properties); every other key is a directory.
/// </summary>
public sealed class JsonPolicyFiles(LinuxRoot root) : IRegistry
{
    public const string Prefix = "FILE";

    /// <summary>The key path for an absolute file or directory path.</summary>
    public static string KeyPathFor(string absolutePath) =>
        Prefix + absolutePath.Replace('/', '\\').TrimEnd('\\');

    public RegistryValueSnapshot ReadValue(string keyPath, string name)
    {
        var file = PolicyFile(keyPath);
        if (!File.Exists(file) || Load(file)[name] is not { } node)
            return RegistryValueSnapshot.Absent;

        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out var number) && number is >= int.MinValue and <= int.MaxValue)
                return new(true, PolicyValueKind.DWord, number.ToString(CultureInfo.InvariantCulture));
            if (value.TryGetValue<string>(out var text))
                return new(true, PolicyValueKind.String, text);
        }

        return new(true, PolicyValueKind.Unsupported, null);
    }

    public void WriteValue(string keyPath, string name, PolicyValueKind kind, string data)
    {
        var file = PolicyFile(keyPath);
        if (!File.Exists(file))
            throw new InvalidOperationException($"Policy file {file} doesn't exist.");

        var policies = Load(file);
        policies[name] = kind switch
        {
            PolicyValueKind.DWord => JsonValue.Create(int.Parse(data, CultureInfo.InvariantCulture)),
            PolicyValueKind.String => JsonValue.Create(data),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only DWord and String values are written."),
        };
        Save(file, policies);
    }

    public void DeleteValue(string keyPath, string name)
    {
        var file = PolicyFile(keyPath);
        if (!File.Exists(file))
            return;
        var policies = Load(file);
        if (policies.Remove(name))
            Save(file, policies);
    }

    public bool KeyExists(string keyPath)
    {
        var (path, isFile) = Resolve(keyPath);
        return isFile ? File.Exists(path) : Directory.Exists(path);
    }

    public void CreateKey(string keyPath)
    {
        var (path, isFile) = Resolve(keyPath);
        if (!isFile)
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
            Save(path, new JsonObject());
    }

    public void DeleteKeyIfEmpty(string keyPath)
    {
        var (path, isFile) = Resolve(keyPath);
        if (isFile)
        {
            if (File.Exists(path) && Load(path).Count == 0)
                File.Delete(path);
        }
        else if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }

    private string PolicyFile(string keyPath)
    {
        var (path, isFile) = Resolve(keyPath);
        return isFile ? path : throw new ArgumentException($"'{keyPath}' isn't a policy file (it must end in .json).", nameof(keyPath));
    }

    private (string Path, bool IsFile) Resolve(string keyPath)
    {
        var separator = keyPath.IndexOf('\\');
        if (separator <= 0 || !keyPath[..separator].Equals(Prefix, StringComparison.OrdinalIgnoreCase) || separator == keyPath.Length - 1)
            throw new ArgumentException($"'{keyPath}' isn't a {Prefix}\\ key path.", nameof(keyPath));

        var parts = keyPath[(separator + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
            throw new ArgumentException($"'{keyPath}' mustn't contain . or .. parts.", nameof(keyPath));

        var absolute = "/" + string.Join('/', parts);
        return (root.Resolve(absolute), absolute.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads a policy file. A file that isn't a JSON object is an error: it's never overwritten blindly.</summary>
    private static JsonObject Load(string file)
    {
        var text = File.ReadAllText(file);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidDataException($"{file} isn't a JSON object, so it's left alone.");
    }

    /// <summary>Writes to a temporary file and swaps it in, so a crash never leaves Chrome a half-written policy.</summary>
    private static void Save(string file, JsonObject policies)
    {
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, policies.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.Move(temporary, file, overwrite: true);
    }
}
