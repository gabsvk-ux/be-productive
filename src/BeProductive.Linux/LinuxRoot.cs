namespace BeProductive.Linux;

/// <summary>
/// Where the Linux file system starts. Always "/" for real; tests point it at a temporary directory holding a
/// fake <c>/proc</c>, <c>/run/systemd/sessions</c>, <c>/etc</c> and so on, so every reader here is testable.
/// </summary>
public sealed class LinuxRoot(string path)
{
    public static readonly LinuxRoot Real = new("/");

    public string Path { get; } = path;

    /// <summary>Maps an absolute Linux path such as <c>/etc/hosts</c> under the root.</summary>
    public string Resolve(string absolute) =>
        Path == "/" ? absolute : System.IO.Path.Combine(Path, absolute.TrimStart('/'));
}
