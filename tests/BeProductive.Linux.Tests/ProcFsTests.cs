namespace BeProductive.Linux.Tests;

public sealed class ProcFsTests : IDisposable
{
    private readonly FakeLinux linux = new();

    public void Dispose() => linux.Dispose();

    [Fact]
    public void Lists_processes_with_their_owner_and_executable_name()
    {
        linux.Process(100, 1000, "/usr/lib/firefox/firefox", "/usr/lib/firefox/firefox --new-window");

        var process = Assert.Single(new ProcFs(linux.Root).Processes());

        Assert.Equal(new LinuxProcess(100, 1000, "firefox", "/usr/lib/firefox/firefox", "/usr/lib/firefox/firefox --new-window"), process);
    }

    [Fact]
    public void Kernel_threads_and_non_process_entries_are_skipped()
    {
        linux.Process(2, 0, executable: null);
        linux.Write("/proc/self/status", "Uid:\t0\t0\t0\t0\n");
        linux.Write("/proc/meminfo", "MemTotal: 1 kB\n");

        Assert.Empty(new ProcFs(linux.Root).Processes());
    }

    [Theory]
    [InlineData("/opt/google/chrome/chrome", "chrome")]
    [InlineData("/usr/share/discord/Discord", "discord")]
    [InlineData("/usr/lib/firefox/firefox (deleted)", "firefox")]
    public void Names_are_the_lowercase_executable_file_name(string path, string expected)
    {
        Assert.Equal(expected, ProcFs.NameOf(path));
    }

    [Fact]
    public void The_real_uid_is_the_first_uid_field()
    {
        Assert.Equal(1000, ProcFs.RealUid(["Name:\tbash", "Uid:\t1000\t0\t0\t0"]));
        Assert.Null(ProcFs.RealUid(["Name:\tbash"]));
    }

    [Fact]
    public void Reads_the_real_proc_of_this_machine()
    {
        var self = new ProcFs(LinuxRoot.Real).Read(Environment.ProcessId);

        if (!OperatingSystem.IsLinux())
            return;
        Assert.NotNull(self);
        Assert.Equal(Environment.ProcessId, self.Pid);
        Assert.False(string.IsNullOrEmpty(self.Name));
    }
}
