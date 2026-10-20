using BeProductive.Core.SafetyNet;

namespace BeProductive.Core.Tests;

public class HostsSectionTests
{
    private const string Original = "# Copyright (c) Microsoft Corp.\r\n127.0.0.1 localhost\r\n";

    [Fact]
    public void Applying_adds_a_marked_section_and_keeps_everything_else()
    {
        var result = HostsSection.Apply(Original, ["YouTube.com", "www.youtube.com"]);

        Assert.StartsWith(Original, result);
        Assert.Contains("0.0.0.0 youtube.com\r\n", result);
        Assert.Contains("0.0.0.0 www.youtube.com\r\n", result);
        Assert.Contains(HostsSection.BeginMarker, result);
        Assert.EndsWith(HostsSection.EndMarker + "\r\n", result);
    }

    [Fact]
    public void Applying_again_replaces_the_section()
    {
        var result = HostsSection.Apply(HostsSection.Apply(Original, ["youtube.com"]), ["reddit.com"]);

        Assert.DoesNotContain("youtube.com", result);
        Assert.Contains("0.0.0.0 reddit.com", result);
        Assert.Single(result.Split(HostsSection.BeginMarker)[1..]);
    }

    [Fact]
    public void Removing_gives_back_the_original_file()
    {
        Assert.Equal(Original, HostsSection.Remove(HostsSection.Apply(Original, ["youtube.com"])));
    }

    [Fact]
    public void Unix_line_endings_are_kept()
    {
        const string unix = "127.0.0.1 localhost\n";

        var applied = HostsSection.Apply(unix, ["youtube.com"]);

        Assert.DoesNotContain("\r", applied);
        Assert.Equal(unix, HostsSection.Remove(applied));
    }

    [Fact]
    public void A_missing_end_marker_removes_only_the_block_lines()
    {
        var damaged = $"{HostsSection.BeginMarker}\r\n0.0.0.0 youtube.com\r\n10.0.0.5 nas.local\r\n";

        Assert.Equal("10.0.0.5 nas.local\r\n", HostsSection.Remove(damaged));
    }

    [Theory]
    [InlineData("evil.com\r\n1.2.3.4 bank.com")]
    [InlineData("not a domain")]
    [InlineData("localhost")]
    public void Invalid_domains_are_refused(string domain) =>
        Assert.Throws<ArgumentException>(() => HostsSection.Apply(Original, [domain]));
}
