namespace BeProductive.Linux.Tests;

public sealed class LinuxUserSessionTests : IDisposable
{
    private readonly FakeLinux linux = new();

    public void Dispose() => linux.Dispose();

    private LoginSession? Active() => new LinuxUserSession(linux.Root).ActiveGraphicalSession();

    [Fact]
    public void Finds_the_active_graphical_session_on_the_main_seat()
    {
        linux.Session("2", 1000, "alice");

        Assert.Equal(new LoginSession("2", 1000, "alice", "wayland", "user", "seat0", true), Active());
    }

    [Fact]
    public void The_login_screen_is_not_a_signed_in_user()
    {
        linux.Session("c1", 120, "gdm", @class: "greeter");

        Assert.Null(Active());
    }

    [Fact]
    public void Text_consoles_ssh_and_background_sessions_are_ignored()
    {
        linux.Session("3", 1000, "alice", type: "tty");
        linux.Session("4", 1000, "alice", type: "x11", active: false);
        linux.Session("5", 1001, "bob", type: "x11", seat: "");

        Assert.Null(Active());
    }

    [Fact]
    public void After_a_user_switch_the_foreground_user_wins()
    {
        linux.Session("2", 1000, "alice", active: false);
        linux.Session("7", 1001, "bob", type: "x11");

        Assert.Equal("bob", Active()?.User);
    }

    [Fact]
    public void No_logind_means_nobody_is_signed_in()
    {
        Assert.Null(Active());
    }

    [Fact]
    public void Ref_fifos_and_unreadable_lines_are_skipped()
    {
        linux.Write("/run/systemd/sessions/2.ref", "");
        linux.Write("/run/systemd/sessions/9", "garbage\nno uid here\n");
        linux.Session("2", 1000, "alice");

        Assert.Single(new LinuxUserSession(linux.Root).Sessions());
    }
}
