namespace BeProductive.Linux.Tests;

public sealed class DesktopAppsTests : IDisposable
{
    private readonly FakeLinux linux = new();

    public void Dispose() => linux.Dispose();

    [Theory]
    [InlineData("/usr/bin/firefox %u", "firefox")]
    [InlineData("\"/opt/My App/myapp\" --flag", "myapp")]
    [InlineData("env BAMF_DESKTOP_FILE_HINT=/x.desktop /snap/bin/spotify %U", "spotify")]
    [InlineData("Discord", "discord")]
    [InlineData("flatpak run org.gimp.GIMP", null)]
    [InlineData("sh -c \"some script\"", null)]
    [InlineData("python3 /opt/tool.py", null)]
    [InlineData("%u", null)]
    public void The_program_is_the_first_real_word_of_Exec(string exec, string? expected)
    {
        Assert.Equal(expected, DesktopApps.ProgramOf(exec));
    }

    [Fact]
    public void Launchers_map_to_the_process_they_really_run()
    {
        var names = DesktopApps.NamesFrom(["[Desktop Entry]", "Type=Application", "Exec=/usr/bin/google-chrome-stable %U", "StartupWMClass=Google-chrome"]);

        Assert.Contains("chrome", names);
        Assert.Contains("google-chrome-stable", names);
        Assert.Contains("google-chrome", names);
    }

    [Fact]
    public void Hidden_entries_links_and_other_groups_give_nothing()
    {
        Assert.Empty(DesktopApps.NamesFrom(["[Desktop Entry]", "Exec=gsd-helper", "NoDisplay=true"]));
        Assert.Empty(DesktopApps.NamesFrom(["[Desktop Entry]", "Exec=thing", "Hidden=true"]));
        Assert.Empty(DesktopApps.NamesFrom(["[Desktop Entry]", "Type=Link", "URL=https://example.com"]));
        Assert.Equal(["main"], DesktopApps.NamesFrom(["[Desktop Entry]", "Exec=main", "[Desktop Action new]", "Exec=other"]));
    }

    [Fact]
    public void Scans_system_flatpak_snap_and_per_user_launchers()
    {
        linux.DesktopEntry("firefox", "Exec=firefox %u");
        linux.DesktopEntry("spotify", "Exec=/snap/bin/spotify", "/var/lib/snapd/desktop/applications");
        linux.DesktopEntry("game", "Exec=/home/alice/Games/game.x86_64", "/home/alice/.local/share/applications");
        linux.DesktopEntry("helper", "Exec=/usr/libexec/helper\nNoDisplay=true");

        var names = new DesktopApps(linux.Root, linux.Time).Names();

        Assert.Contains("firefox", names);
        Assert.Contains("spotify", names);
        Assert.Contains("game.x86_64", names);
        Assert.DoesNotContain("helper", names);
    }

    [Fact]
    public void Rescans_after_a_minute()
    {
        var apps = new DesktopApps(linux.Root, linux.Time);
        Assert.Empty(apps.Names());

        linux.DesktopEntry("steam", "Exec=/usr/games/steam %U");
        Assert.Empty(apps.Names());

        linux.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Contains("steam", apps.Names());
    }
}
