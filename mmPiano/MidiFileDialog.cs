using System.Diagnostics;
using System.Runtime.InteropServices;

namespace mmPiano;

// The Windows file picker for .mid/.midi files, via comdlg32
// GetOpenFileName — no WinForms reference, so the project keeps
// its net10.0 target. Windows-only; returns null anywhere else
// or when the user cancels. Remembers the last folder picked
// from, so reopening the dialog returns there.
//
// (v88 declared the file buffer as a StringBuilder field, which
// .NET 10 cannot marshal in a struct at all — SizeOf and the
// P/Invoke both threw "cannot be marshaled as an unmanaged
// structure". The buffer is now unmanaged memory held in an
// IntPtr, and the struct carries the three trailing fields
// (lpvReserved/dwReserved/FlagsEx) so its size is the native
// 152 bytes on 64-bit.)
internal static class MidiFileDialog
{
    private static string? _lastDir;

    public static string? PickMidiFile()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        string initialDir = _lastDir
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            ?? "";
        // The dialog writes the picked path into this buffer.
        IntPtr fileBuffer = Marshal.AllocHGlobal(1024 * 2);
        try
        {
            for (int i = 0; i < 1024 * 2; i++)
                Marshal.WriteByte(fileBuffer, i, 0);
            var ofn = new OpenFileName
            {
                lStructSize = (uint)Marshal.SizeOf<OpenFileName>(),
                hwndOwner = Process.GetCurrentProcess().MainWindowHandle,
                lpstrFilter = "MIDI Files (*.mid;*.midi)\0*.mid;*.midi\0All Files (*.*)\0*.*\0\0",
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = 1024,
                lpstrFileTitle = IntPtr.Zero,
                nMaxFileTitle = 0,
                lpstrInitialDir = initialDir,
                lpstrTitle = "Open MIDI file",
                Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008 | 0x00000004, // Explorer, file/path must exist, don't change dir, hide read-only
                lpstrDefExt = "mid",
            };
            if (!GetOpenFileName(ref ofn))
                return null;
            string? path = Marshal.PtrToStringUni(fileBuffer);
            if (string.IsNullOrEmpty(path))
                return null;
            _lastDir = Path.GetDirectoryName(path);
            return path;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public IntPtr lpstrFile;
        public uint nMaxFile;
        public IntPtr lpstrFileTitle;
        public uint nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr lpvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }
}
