using System.Text.RegularExpressions;

namespace BeProductive.Core.SafetyNet;

/// <summary>
/// The app's own block of lines in the hosts file, between two marker comments.
/// Nothing outside the markers is ever touched. restore.ps1 uses the same markers.
/// </summary>
public static partial class HostsSection
{
    public const string BeginMarker = "# BEGIN BeProductive (managed automatically, do not edit)";
    public const string EndMarker = "# END BeProductive";

    private const string BlockAddress = "0.0.0.0";

    /// <summary>Replaces the app's section (or adds one at the end) with entries for <paramref name="domains"/>.</summary>
    public static string Apply(string content, IEnumerable<string> domains)
    {
        var newline = NewlineOf(content);
        var lines = SplitLines(Remove(content));

        var entries = domains
            .Select(domain => domain.Trim().ToLowerInvariant())
            .Distinct()
            .Select(domain => IsValidDomain(domain)
                ? $"{BlockAddress} {domain}"
                : throw new ArgumentException($"'{domain}' isn't a valid domain name.", nameof(domains)))
            .ToList();

        lines.Add(BeginMarker);
        lines.AddRange(entries);
        lines.Add(EndMarker);
        return string.Join(newline, lines) + newline;
    }

    /// <summary>Removes the app's section, leaving every other line as it was.</summary>
    public static string Remove(string content)
    {
        var newline = NewlineOf(content);
        var lines = SplitLines(content);
        var kept = new List<string>(lines.Count);

        var inside = false;
        foreach (var line in lines)
        {
            if (!inside && line.Trim() == BeginMarker)
            {
                inside = true;
                continue;
            }

            if (inside)
            {
                if (line.Trim() == EndMarker)
                {
                    inside = false;
                    continue;
                }

                // If someone deleted the end marker, stop at the first line that isn't one of ours.
                if (line.StartsWith(BlockAddress + " ", StringComparison.Ordinal))
                    continue;
                inside = false;
            }

            kept.Add(line);
        }

        return kept.Count == 0 ? "" : string.Join(newline, kept) + newline;
    }

    private static bool IsValidDomain(string domain) => domain.Length <= 253 && DomainPattern().IsMatch(domain);

    private static string NewlineOf(string content) =>
        content.Contains('\n') && !content.Contains("\r\n") ? "\n" : "\r\n";

    private static List<string> SplitLines(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainPattern();
}
