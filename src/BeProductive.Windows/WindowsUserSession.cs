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
    /// True only when a real person is signed in at the console AND the session is unlocked. Before login
    /// (the logon screen) or while locked, the only apps on screen are the logon / lock-screen UI, which
    /// must never be killed — so enforcement that ends processes stands back until this is true.
    /// </summary>
    public static bool InteractiveSessionIsActive()
    {
        var session = ActiveSessionId();
        if (session < 0)
            return false;

        // No user token => nobody is logged in yet (the logon screen).
        if (!WTSQueryUserToken((uint)session, out var token))
            return false;
        CloseHandle(token);

        return !IsSessionLocked((uint)session);
    }

    /// <summary>Whether the session is showing the lock screen. Treated as "not locked" if it can't be read.</summary>
    private static bool IsSessionLocked(uint session)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, WTSSessionInfoEx, out var buffer, out var bytes) || buffer == IntPtr.Zero)
            return false;

        try
        {
            // WTSINFOEXW: Level (4 bytes) + padding, then WTSINFOEX_LEVEL1_W whose first field is SessionId,
            // followed by SessionState and then SessionFlags. On Windows 8+ SessionFlags is 0 when locked.
            if (bytes < 20)
                return false;
            var sessionFlags = Marshal.ReadInt32(buffer, 16);
            return sessionFlags == WTS_SESSIONSTATE_LOCK;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private const int WTSSessionInfoEx = 25;
    private const int WTS_SESSIONSTATE_LOCK = 0;

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
