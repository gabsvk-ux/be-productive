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

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
