using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BeProductive.Windows;

/// <summary>Who is signed in at the screen, and in which session. The service runs as SYSTEM, so it has to ask.</summary>
[SupportedOSPlatform("windows")]
public static partial class WindowsUserSession
{
    /// <summary>The session id shown on the physical console, or -1 if nobody is signed in.</summary>
    public static int ActiveSessionId()
    {
        var id = WTSGetActiveConsoleSessionId();
        return id == 0xFFFFFFFF ? -1 : (int)id;
    }

    /// <summary>The SID of the user in the active console session, as a string, or null if none.</summary>
    public static string? ActiveUserSid()
    {
        var session = ActiveSessionId();
        if (session < 0)
            return null;

        if (!WTSQueryUserToken((uint)session, out var token))
            return null;

        try
        {
            using var identity = new System.Security.Principal.WindowsIdentity(token);
            return identity.User?.Value;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>
    /// Whether a real person is signed in at the console. Before anyone logs in (the logon screen) there is
    /// no user token, and the only things on screen are the logon UI — which must never be killed — so the
    /// process guard stands back until someone is signed in. While the session is merely *locked* a user is
    /// still signed in, so this stays true; the lock screen is kept safe by the never-kill list instead,
    /// which is far more reliable than the version-dependent Windows "locked" flag.
    /// </summary>
    public static bool IsUserSignedIn()
    {
        var session = ActiveSessionId();
        if (session < 0)
            return false;

        if (!WTSQueryUserToken((uint)session, out var token))
            return false;
        CloseHandle(token);
        return true;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
