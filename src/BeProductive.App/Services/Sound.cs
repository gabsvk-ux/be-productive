using System.Runtime.InteropServices;

namespace BeProductive.App.Services;

public static partial class Sound
{
    private const uint Asterisk = 0x40;
    private const uint Exclamation = 0x30;

    /// <summary>A gentle chime: a focus block ended.</summary>
    public static void Chime() => Play(Asterisk);

    /// <summary>A more insistent sound: a break ended and the next block needs picking.</summary>
    public static void Alert() => Play(Exclamation);

    private static void Play(uint type)
    {
        if (OperatingSystem.IsWindows())
            MessageBeep(type);
        else if (OperatingSystem.IsLinux())
            PlayFreedesktop(type == Asterisk ? "complete" : "dialog-warning");
    }

    /// <summary>
    /// Plays a sound from the desktop's sound theme with canberra-gtk-play (installed with GNOME and most
    /// desktops), without waiting for it. No sound if it isn't installed — a missing chime is never an error.
    /// </summary>
    private static void PlayFreedesktop(string soundId)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("canberra-gtk-play") { UseShellExecute = false };
            start.ArgumentList.Add("--id=" + soundId);
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        catch
        {
            // Not installed, or no sound server: stay quiet.
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint type);
}
