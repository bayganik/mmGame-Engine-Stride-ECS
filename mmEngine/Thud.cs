using System.Runtime.InteropServices;

namespace mmEngine;

// Fire-and-forget WAV playback via WinMM (Windows-only, like the game).
// Async, so it never blocks the game loop. Rapid hits share one thud:
// at most one every 80ms, so chain reactions don't turn into noise.
public static class Thud
{
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    private const uint SndFilename = 0x00020000;
    private const uint SndAsync = 0x0001;

    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Thud.wav");
    private static DateTime _lastPlayed = DateTime.MinValue;

    public static void Play()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPlayed).TotalMilliseconds < 80) return;
        _lastPlayed = now;
        PlaySound(Path, IntPtr.Zero, SndFilename | SndAsync);
    }
}
