using System.Runtime.InteropServices;

namespace YtMusicMediaGuard;

internal static class ChordSender
{
    public static void Send(Chord chord)
    {
        var mods = new List<ushort>();
        if (chord.Ctrl) mods.Add(0x11);
        if (chord.Alt) mods.Add(0x12);
        if (chord.Shift) mods.Add(0x10);
        if (chord.Win) mods.Add(0x5B);

        var inputs = new List<INPUT>(mods.Count * 2 + 2);
        foreach (var key in mods) inputs.Add(Key(key, up: false));
        inputs.Add(Key(chord.VirtualKey, up: false));
        inputs.Add(Key(chord.VirtualKey, up: true));
        for (var i = mods.Count - 1; i >= 0; i--) inputs.Add(Key(mods[i], up: true));

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count)
            throw new InvalidOperationException($"SendInput delivered {sent} of {inputs.Count} events (error {Marshal.GetLastWin32Error()}).");
    }

    public static int InputSize => Marshal.SizeOf<INPUT>();

    private static INPUT Key(ushort virtualKey, bool up)
    {
        var flags = up ? KeyUp : 0u;
        if (Chord.IsExtended(virtualKey) || virtualKey == 0x5B) flags |= ExtendedKey;
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = (ushort)MapVirtualKey(virtualKey, 0),
                    dwFlags = flags,
                },
            },
        };
    }

    private const uint InputKeyboard = 1;
    private const uint ExtendedKey = 0x0001;
    private const uint KeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
