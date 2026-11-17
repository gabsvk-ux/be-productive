using BeProductive.Core.Enforcement;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Linux.Tests;

public sealed class LinuxPlatformTests : IDisposable
{
    private readonly FakeLinux linux = new();

    public void Dispose() => linux.Dispose();

    [Fact]
    public void The_hosts_section_is_added_and_removed_leaving_the_rest_alone()
    {
        const string original = "127.0.0.1 localhost\n::1 localhost ip6-localhost\n";
        linux.Write("/etc/hosts", original);
        var hosts = new LinuxHostsFile(linux.Root);

        hosts.Write(HostsSection.Apply(hosts.Read(), ["youtube.com"]));
        Assert.Contains("0.0.0.0 youtube.com\n", hosts.Read());
        Assert.DoesNotContain("\r", hosts.Read());

        hosts.Write(HostsSection.Remove(hosts.Read()));
        Assert.Equal(original, hosts.Read());
    }

    [Fact]
    public void Flushing_dns_without_a_cache_daemon_does_nothing()
    {
        new LinuxDnsCache(linux.Root).Flush();
    }

    [Fact]
    public void Chrome_policies_go_to_chrome_and_chromium_and_admin_tools_have_none()
    {
        var policies = new LinuxEnforcementPolicies();

        var chrome = policies.For(PolicyGroup.Chrome);
        Assert.Equal(6, chrome.Count);
        Assert.All(chrome, value => Assert.StartsWith(@"FILE\etc\", value.KeyPath));
        Assert.Contains(chrome, value => value is { Name: "DnsOverHttpsMode", Data: "off" });
        Assert.Empty(policies.For(PolicyGroup.AdminTools));
    }

    [Fact]
    public void The_watchdog_recognises_the_published_app_and_a_development_run()
    {
        Assert.True(LinuxAppLauncher.IsApp(new LinuxProcess(1, 1000, "beproductive", "/opt/be-productive/App/BeProductive", "")));
        Assert.True(LinuxAppLauncher.IsApp(new LinuxProcess(1, 1000, "dotnet", "/usr/lib/dotnet/dotnet", "dotnet bin/BeProductive.dll")));
        Assert.False(LinuxAppLauncher.IsApp(new LinuxProcess(1, 1000, "dotnet", "/usr/lib/dotnet/dotnet", "dotnet BeProductive.Service.dll")));
    }
}
