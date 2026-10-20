using System.Globalization;
using System.Runtime.Versioning;
using BeProductive.Core.SafetyNet;
using Microsoft.Win32;

namespace BeProductive.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    public RegistryValueSnapshot ReadValue(string keyPath, string name)
    {
        using var key = OpenKey(keyPath, writable: false);
        var value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (key is null || value is null)
            return RegistryValueSnapshot.Absent;

        return key.GetValueKind(name) switch
        {
            RegistryValueKind.DWord => new(true, PolicyValueKind.DWord, ((int)value).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.String => new(true, PolicyValueKind.String, (string)value),
            _ => new(true, PolicyValueKind.Unsupported, null),
        };
    }

    public void WriteValue(string keyPath, string name, PolicyValueKind kind, string data)
    {
        using var key = OpenKey(keyPath, writable: true)
            ?? throw new InvalidOperationException($"Registry key {keyPath} doesn't exist.");
        switch (kind)
        {
            case PolicyValueKind.DWord:
                key.SetValue(name, int.Parse(data, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                break;
            case PolicyValueKind.String:
                key.SetValue(name, data, RegistryValueKind.String);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only DWord and String values are written.");
        }
    }

    public void DeleteValue(string keyPath, string name)
    {
        using var key = OpenKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public bool KeyExists(string keyPath)
    {
        using var key = OpenKey(keyPath, writable: false);
        return key is not null;
    }

    public void CreateKey(string keyPath)
    {
        var (hive, subKey) = Split(keyPath);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var _ = root.CreateSubKey(subKey, writable: true);
    }

    public void DeleteKeyIfEmpty(string keyPath)
    {
        using (var key = OpenKey(keyPath, writable: false))
        {
            if (key is null || key.ValueCount > 0 || key.SubKeyCount > 0)
                return;
        }

        var (hive, subKey) = Split(keyPath);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        root.DeleteSubKey(subKey, throwOnMissingSubKey: false);
    }

    private static RegistryKey? OpenKey(string keyPath, bool writable)
    {
        var (hive, subKey) = Split(keyPath);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return root.OpenSubKey(subKey, writable);
    }

    private static (RegistryHive Hive, string SubKey) Split(string keyPath)
    {
        var separator = keyPath.IndexOf('\\');
        if (separator <= 0 || separator == keyPath.Length - 1)
            throw new ArgumentException($"'{keyPath}' isn't a full registry key path.", nameof(keyPath));

        var hive = keyPath[..separator].ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKEY_USERS" => RegistryHive.Users,
            "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            var other => throw new ArgumentException($"Unsupported registry hive '{other}'.", nameof(keyPath)),
        };
        return (hive, keyPath[(separator + 1)..]);
    }
}
