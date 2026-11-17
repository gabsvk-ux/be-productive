using BeProductive.Core;
using BeProductive.Core.Enforcement;
using BeProductive.Core.SafetyNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BeProductive.Linux.Tests;

public sealed class JsonPolicyFilesTests : IDisposable
{
    private const string ChromeFile = "/etc/opt/chrome/policies/managed/be-productive.json";
    private static readonly string ChromeKey = JsonPolicyFiles.KeyPathFor(ChromeFile);

    private readonly FakeLinux linux = new();
    private readonly JsonPolicyFiles files;

    public JsonPolicyFilesTests()
    {
        files = new JsonPolicyFiles(linux.Root);
        Directory.CreateDirectory(linux.Root.Resolve("/etc"));
    }

    public void Dispose() => linux.Dispose();

    [Fact]
    public void Key_paths_are_the_file_path_with_backslashes()
    {
        Assert.Equal(@"FILE\etc\opt\chrome\policies\managed\be-productive.json", ChromeKey);
    }

    [Fact]
    public void Values_round_trip_as_json_numbers_and_strings()
    {
        files.CreateKey(ChromeKey);
        files.WriteValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "1");
        files.WriteValue(ChromeKey, "DnsOverHttpsMode", PolicyValueKind.String, "off");

        Assert.Equal(new RegistryValueSnapshot(true, PolicyValueKind.DWord, "1"), files.ReadValue(ChromeKey, "IncognitoModeAvailability"));
        Assert.Equal(new RegistryValueSnapshot(true, PolicyValueKind.String, "off"), files.ReadValue(ChromeKey, "DnsOverHttpsMode"));
        Assert.Equal(RegistryValueSnapshot.Absent, files.ReadValue(ChromeKey, "Missing"));

        var json = File.ReadAllText(linux.Root.Resolve(ChromeFile));
        Assert.Contains("\"IncognitoModeAvailability\": 1", json);
        Assert.Contains("\"DnsOverHttpsMode\": \"off\"", json);
    }

    [Fact]
    public void Values_of_other_json_kinds_are_reported_as_unsupported()
    {
        linux.Write(ChromeFile, """{ "URLBlocklist": ["a.com"] }""");

        Assert.Equal(PolicyValueKind.Unsupported, files.ReadValue(ChromeKey, "URLBlocklist").Kind);
    }

    [Fact]
    public void A_policy_file_that_is_not_a_json_object_is_never_overwritten()
    {
        linux.Write(ChromeFile, "[1, 2]");

        Assert.Throws<InvalidDataException>(() => files.WriteValue(ChromeKey, "X", PolicyValueKind.DWord, "1"));
        Assert.Equal("[1, 2]", File.ReadAllText(linux.Root.Resolve(ChromeFile)));
    }

    [Fact]
    public void Paths_outside_the_file_prefix_or_with_dot_dot_are_refused()
    {
        Assert.Throws<ArgumentException>(() => files.KeyExists(@"HKEY_LOCAL_MACHINE\SOFTWARE"));
        Assert.Throws<ArgumentException>(() => files.CreateKey(@"FILE\etc\..\root\x.json"));
        Assert.Throws<ArgumentException>(() => files.WriteValue(@"FILE\etc\opt", "X", PolicyValueKind.DWord, "1"));
    }

    [Fact]
    public void Through_the_safety_net_restoring_removes_the_file_and_the_directories_created_for_it()
    {
        var data = linux.Root.Resolve("/var/lib/be-productive");
        Directory.CreateDirectory(data);
        var paths = new AppPaths(data);
        var changes = new SystemChanges(
            new JsonJournalStore(paths), files, new LinuxHostsFile(linux.Root), new LinuxDnsCache(linux.Root),
            new EnforcementGate(Options.Create(new EnforcementOptions { DryRun = false }), paths, NullLogger<EnforcementGate>.Instance),
            Options.Create(new SafetyNetOptions()), linux.Time, NullLogger<SystemChanges>.Instance);

        foreach (var value in new LinuxEnforcementPolicies().For(PolicyGroup.Chrome))
            Assert.True(changes.SetRegistryValue(value.KeyPath, value.Name, value.Kind, value.Data));

        Assert.True(File.Exists(linux.Root.Resolve(ChromeFile)));
        Assert.True(File.Exists(linux.Root.Resolve("/etc/chromium/policies/managed/be-productive.json")));

        var report = changes.RestoreAll("test");

        Assert.Equal(0, report.Failed);
        Assert.False(File.Exists(linux.Root.Resolve(ChromeFile)));
        Assert.False(Directory.Exists(linux.Root.Resolve("/etc/opt")));
        Assert.False(Directory.Exists(linux.Root.Resolve("/etc/chromium")));
        Assert.True(Directory.Exists(linux.Root.Resolve("/etc")));
        Assert.False(File.Exists(paths.JournalFile));
    }

    [Fact]
    public void Restoring_keeps_a_directory_and_values_that_were_already_there()
    {
        linux.Write(ChromeFile, """{ "HomepageLocation": "https://example.com", "IncognitoModeAvailability": 0 }""");
        var data = linux.Root.Resolve("/var/lib/be-productive");
        var paths = new AppPaths(data);
        Directory.CreateDirectory(data);
        var changes = new SystemChanges(
            new JsonJournalStore(paths), files, new LinuxHostsFile(linux.Root), new LinuxDnsCache(linux.Root),
            new EnforcementGate(Options.Create(new EnforcementOptions { DryRun = false }), paths, NullLogger<EnforcementGate>.Instance),
            Options.Create(new SafetyNetOptions()), linux.Time, NullLogger<SystemChanges>.Instance);

        changes.SetRegistryValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "1");
        changes.SetRegistryValue(ChromeKey, "DnsOverHttpsMode", PolicyValueKind.String, "off");
        changes.RestoreAll("test");

        Assert.Equal(new RegistryValueSnapshot(true, PolicyValueKind.DWord, "0"), files.ReadValue(ChromeKey, "IncognitoModeAvailability"));
        Assert.Equal(new RegistryValueSnapshot(true, PolicyValueKind.String, "https://example.com"), files.ReadValue(ChromeKey, "HomepageLocation"));
        Assert.Equal(RegistryValueSnapshot.Absent, files.ReadValue(ChromeKey, "DnsOverHttpsMode"));
    }
}
