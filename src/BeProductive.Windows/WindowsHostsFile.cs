using System.Runtime.Versioning;
using System.Text;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsHostsFile : IHostsFile
{
    private static string FilePath => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

    public string Read() => File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";

    // Written in place rather than swapped in, so the file keeps its own permissions.
    public void Write(string content) => File.WriteAllText(FilePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
