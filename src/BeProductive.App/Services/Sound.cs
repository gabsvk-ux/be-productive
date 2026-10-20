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
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint type);
}
