namespace BeProductive.Linux;

/// <summary>A logind session: who is signed in, and how.</summary>
public sealed record LoginSession(string Id, int Uid, string User, string Type, string Class, string Seat, bool Active);

/// <summary>
/// Who is signed in at the screen. The service runs as root under systemd, so it asks logind — by reading the
/// session files logind keeps in <c>/run/systemd/sessions</c> (key=value lines), which needs no D-Bus.
/// </summary>
public sealed class LinuxUserSession(LinuxRoot root)
{
    private static readonly HashSet<string> GraphicalTypes = new(StringComparer.OrdinalIgnoreCase) { "x11", "wayland", "mir" };

    /// <summary>
    /// The graphical session in front of the physical screen (seat0) that is in the foreground, or null when
    /// nobody is signed in there. The login screen itself is a logind session of class "greeter", which is
    /// never returned — so nothing is ever policed at the login screen. While the screen is merely locked the
    /// user's session is still the active one; lock screens are protected by the never-kill list instead.
    /// </summary>
    public LoginSession? ActiveGraphicalSession() =>
        Sessions().FirstOrDefault(session =>
            session.Active &&
            session.Seat == "seat0" &&
            session.Class == "user" &&
            GraphicalTypes.Contains(session.Type));

    public IReadOnlyList<LoginSession> Sessions()
    {
        var directory = root.Resolve("/run/systemd/sessions");
        if (!Directory.Exists(directory))
            return [];

        var result = new List<LoginSession>();
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            // logind also keeps "<id>.ref" FIFOs here; only plain session files have key=value contents.
            if (System.IO.Path.GetExtension(file).Length > 0)
                continue;
            try
            {
                if (Parse(System.IO.Path.GetFileName(file), File.ReadAllLines(file)) is { } session)
                    result.Add(session);
            }
            catch (IOException)
            {
                // The session ended while we read it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return result;
    }

    internal static LoginSession? Parse(string id, IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
                values[line[..separator]] = line[(separator + 1)..];
        }

        if (!values.TryGetValue("UID", out var uidText) || !int.TryParse(uidText, out var uid))
            return null;

        return new LoginSession(
            id,
            uid,
            values.GetValueOrDefault("USER", ""),
            values.GetValueOrDefault("TYPE", ""),
            values.GetValueOrDefault("CLASS", ""),
            values.GetValueOrDefault("SEAT", ""),
            values.GetValueOrDefault("ACTIVE", "0") == "1" && values.GetValueOrDefault("STATE", "active") != "closing");
    }
}
