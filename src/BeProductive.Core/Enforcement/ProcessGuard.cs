namespace BeProductive.Core.Enforcement;

/// <param name="Name">Normalized program name (lowercase, no ".exe").</param>
public sealed record RunningProcess(int Pid, string Name);

/// <summary>Lists and ends programs in the signed-in user's own session. Never touches system processes.</summary>
public interface IProcessGuard
{
    IReadOnlyList<RunningProcess> UserProcesses();

    /// <summary>Ends the process. Returns false if it was already gone or couldn't be ended.</summary>
    bool Kill(int pid);
}

/// <summary>Lists nothing and ends nothing: for systems without an implementation yet.</summary>
public sealed class NullProcessGuard : IProcessGuard
{
    public IReadOnlyList<RunningProcess> UserProcesses() => [];

    public bool Kill(int pid) => false;
}
