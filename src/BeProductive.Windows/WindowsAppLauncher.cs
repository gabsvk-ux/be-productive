using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BeProductive.Core;
using BeProductive.Core.Enforcement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BeProductive.Windows;

/// <summary>
/// Checks for and (re)launches the desktop app in the signed-in user's session. The service runs as SYSTEM
/// with no desktop, so starting a visible app means getting the user's token and calling CreateProcessAsUser.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsAppLauncher : IAppLauncher
{
    // The app's process name (BeProductive.exe → "beproductive").
    private const string AppProcessName = "beproductive";

    private readonly string appPath;
    private readonly ILogger<WindowsAppLauncher> logger;

    public WindowsAppLauncher(IConfiguration configuration, AppPaths paths, ILogger<WindowsAppLauncher> logger)
    {
        this.logger = logger;
        appPath = ResolveAppPath(configuration);
    }

    public bool IsRunning()
    {
        var session = WindowsUserSession.ActiveSessionId();
        if (session < 0)
            return false;

        foreach (var process in Process.GetProcessesByName(AppProcessName))
        {
            try
            {
                if (process.SessionId == session)
                    return true;
            }
            catch
            {
                // Can't read it; ignore.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    public bool TryLaunch()
    {
        if (appPath.Length == 0 || !File.Exists(appPath))
        {
            logger.LogError("Can't relaunch the app: the executable wasn't found at '{Path}'. Set Watchdog:AppPath.", appPath);
            return false;
        }

        var session = (uint)WindowsUserSession.ActiveSessionId();
        if (!WTSQueryUserToken(session, out var token))
        {
            logger.LogError("Can't relaunch the app: couldn't get the user's token (error {Error})", Marshal.GetLastWin32Error());
            return false;
        }

        var environmentBlock = IntPtr.Zero;
        try
        {
            CreateEnvironmentBlock(out environmentBlock, token, inherit: false);

            var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            var created = CreateProcessAsUser(
                token, appPath, $"\"{appPath}\"", IntPtr.Zero, IntPtr.Zero, false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW, environmentBlock,
                Path.GetDirectoryName(appPath), ref startupInfo, out var processInfo);

            if (!created)
            {
                logger.LogError("Can't relaunch the app: CreateProcessAsUser failed (error {Error})", Marshal.GetLastWin32Error());
                return false;
            }

            CloseHandle(processInfo.hThread);
            CloseHandle(processInfo.hProcess);
            return true;
        }
        finally
        {
            if (environmentBlock != IntPtr.Zero)
                DestroyEnvironmentBlock(environmentBlock);
            CloseHandle(token);
        }
    }

    /// <summary>Config <c>Watchdog:AppPath</c>, else the app published next to the service at <c>..\App\BeProductive.exe</c>.</summary>
    private static string ResolveAppPath(IConfiguration configuration)
    {
        if (configuration["Watchdog:AppPath"] is { Length: > 0 } configured)
            return configured;

        var serviceDirectory = AppContext.BaseDirectory;
        var guess = Path.GetFullPath(Path.Combine(serviceDirectory, "..", "App", "BeProductive.exe"));
        return guess;
    }

    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        IntPtr token, string? applicationName, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
