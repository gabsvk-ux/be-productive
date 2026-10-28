using BeProductive.Core.Enforcement;
using BeProductive.Core.SafetyNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BeProductive.Core.Tests;

public sealed class SystemChangesTests : IDisposable
{
    private const string ChromeKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google\Chrome";
    private const string PoliciesKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies";
    private const string OriginalHosts = "127.0.0.1 localhost\r\n";

    private readonly TempDirectory directory = new();
    private readonly FakeRegistry registry = new();
    private readonly FakeHostsFile hosts = new(OriginalHosts);
    private readonly FakeDnsCache dns = new();
    private readonly FakeTimeProvider time = new();

    public void Dispose() => directory.Dispose();

    private SystemChanges CreateChanges(bool dryRun = false) =>
        new(
            new JsonJournalStore(directory.AppPaths),
            registry,
            hosts,
            dns,
            Gates.Create(directory.AppPaths, dryRun),
            Options.Create(new SafetyNetOptions { DefaultLease = TimeSpan.FromMinutes(15) }),
            time,
            NullLogger<SystemChanges>.Instance);

    [Fact]
    public void A_dry_run_changes_nothing()
    {
        var changes = CreateChanges(dryRun: true);

        Assert.False(changes.SetRegistryValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "1"));
        Assert.False(changes.SetHostsBlocklist(["youtube.com"]));

        Assert.Empty(registry.Values);
        Assert.Equal(OriginalHosts, hosts.Content);
        Assert.False(File.Exists(directory.AppPaths.JournalFile));
    }

    [Fact]
    public void Restoring_puts_an_existing_value_back()
    {
        registry.CreateKey(ChromeKey);
        registry.WriteValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "0");
        var changes = CreateChanges();

        changes.SetRegistryValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "1");
        Assert.Equal((PolicyValueKind.DWord, "1"), registry.Values[(ChromeKey, "IncognitoModeAvailability")]);

        changes.RestoreAll("test");
        Assert.Equal((PolicyValueKind.DWord, "0"), registry.Values[(ChromeKey, "IncognitoModeAvailability")]);
    }

    [Fact]
    public void Restoring_deletes_new_values_and_the_keys_created_for_them()
    {
        var changes = CreateChanges();

        changes.SetRegistryValue(ChromeKey, "DnsOverHttpsMode", PolicyValueKind.String, "off");
        changes.SetRegistryValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "1");
        Assert.Contains(ChromeKey, registry.Keys);

        var report = changes.RestoreAll("test");

        Assert.Equal(new RestoreReport(4, 0), report);
        Assert.Empty(registry.Values);
        Assert.DoesNotContain(ChromeKey, registry.Keys);
        Assert.DoesNotContain(PoliciesKey + @"\Google", registry.Keys);
        Assert.Contains(PoliciesKey, registry.Keys);
        Assert.False(File.Exists(directory.AppPaths.JournalFile));
    }

    [Fact]
    public void Changing_a_value_twice_still_restores_the_true_original()
    {
        var changes = CreateChanges();

        changes.SetRegistryValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "1");
        changes.SetRegistryValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "2");
        changes.RestoreAll("test");

        Assert.Empty(registry.Values);
    }

    [Fact]
    public void A_change_is_recorded_before_it_is_made()
    {
        registry.FailWrites = true;
        var changes = CreateChanges();

        Assert.Throws<IOException>(() => changes.SetRegistryValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "1"));
        Assert.True(File.Exists(directory.AppPaths.JournalFile));

        Assert.Equal(0, changes.RestoreAll("test").Failed);
        Assert.DoesNotContain(ChromeKey, registry.Keys);
    }

    [Fact]
    public void The_hosts_file_is_restored_and_the_dns_cache_flushed()
    {
        var changes = CreateChanges();

        changes.SetHostsBlocklist(["youtube.com"]);
        Assert.Contains("0.0.0.0 youtube.com", hosts.Content);

        changes.RestoreAll("test");

        Assert.Equal(OriginalHosts, hosts.Content);
        Assert.Equal(2, dns.Flushes);
    }

    [Fact]
    public void Changes_are_undone_once_their_lease_expires()
    {
        var changes = CreateChanges();
        changes.SetHostsBlocklist(["youtube.com"]);

        time.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(0, changes.RestoreIfExpired().Restored);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, changes.RestoreIfExpired().Restored);
        Assert.Equal(OriginalHosts, hosts.Content);
    }

    [Fact]
    public void Renewing_the_lease_keeps_changes_in_place()
    {
        var changes = CreateChanges();
        changes.SetHostsBlocklist(["youtube.com"]);

        changes.RenewLease(time.GetUtcNow() + TimeSpan.FromHours(2));
        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, changes.RestoreIfExpired().Restored);
    }

    [Fact]
    public void Undo_steps_that_fail_are_kept_and_retried()
    {
        var changes = CreateChanges();
        changes.SetRegistryValue(ChromeKey, "IncognitoModeAvailability", PolicyValueKind.DWord, "1");
        registry.FailDeletes = true;

        var first = changes.RestoreAll("test");
        Assert.Equal(1, first.Failed);
        Assert.Equal(1, changes.Describe().Count);

        registry.FailDeletes = false;
        Assert.Equal(0, changes.RestoreAll("test").Failed);
        Assert.Empty(registry.Values);
    }

    [Fact]
    public void A_restarted_service_can_undo_what_the_previous_run_applied()
    {
        CreateChanges().SetHostsBlocklist(["youtube.com"]);

        var report = CreateChanges().RestoreAll("leftovers");

        Assert.Equal(1, report.Restored);
        Assert.Equal(OriginalHosts, hosts.Content);
    }

    [Fact]
    public void Values_of_kinds_that_cannot_be_restored_are_left_alone()
    {
        registry.CreateKey(ChromeKey);
        registry.Values[(ChromeKey, "Odd")] = (PolicyValueKind.Unsupported, "");
        var changes = CreateChanges();

        Assert.Throws<InvalidOperationException>(() => changes.SetRegistryValue(ChromeKey, "Odd", PolicyValueKind.DWord, "1"));
        Assert.Equal(0, changes.Describe().Count);
    }

    [Fact]
    public void The_panic_file_switches_enforcement_off()
    {
        var paths = directory.AppPaths;
        File.WriteAllText(paths.PanicFile, "");

        Assert.Equal(EnforcementMode.Panic, Gates.Create(paths).Mode);
        Assert.Equal(EnforcementMode.Live, Gates.Create(paths, panicFileEnabled: false).Mode);
        Assert.Equal(EnforcementMode.DryRun, Gates.Create(paths, dryRun: true).Mode);
    }

    [Fact]
    public void The_alternate_user_writable_panic_file_also_switches_enforcement_off()
    {
        var paths = directory.AppPaths;
        Assert.Equal(EnforcementMode.Live, Gates.Create(paths).Mode);

        File.WriteAllText(paths.PublicPanicFile, "");

        Assert.Contains(paths.PublicPanicFile, paths.PanicFiles);
        Assert.Equal(EnforcementMode.Panic, Gates.Create(paths).Mode);
    }
}
