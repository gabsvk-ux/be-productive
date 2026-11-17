using System.Text;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Linux;

public sealed class LinuxHostsFile(LinuxRoot root) : IHostsFile
{
    private string FilePath => root.Resolve("/etc/hosts");

    public string Read() => File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";

    // Written in place rather than swapped in, so the file keeps its owner, mode and SELinux label.
    public void Write(string content) => File.WriteAllText(FilePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
