using System.Runtime.InteropServices;

namespace YtMusicMediaGuard;

/// <summary>
/// Volume from a Logitech Pro X wireless receiver stays with Windows.
/// The keyboard hook cannot see the device, so raw input supplies it.
/// </summary>
internal sealed class HeadsetVolume : IDisposable
{
    private const uint WmInput = 0x00FF;
    private const uint WmDeviceChange = 0x00FE;
    private const uint WmTimer = 0x0113;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevDevNotify = 0x00002000;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RimTypeKeyboard = 1;
    private const uint RimTypeHid = 2;
    private const int HwndMessage = -3;
    private const int PendingMs = 40;

    private readonly Action<MediaAction, uint, bool> _commit;
    private readonly WndProc _wndProc;
    private readonly Dictionary<IntPtr, bool> _headset = new();
    private string[] _detections = VolumeHeadset.DefaultDetections;
    private string[] _exclusions = VolumeHeadset.DefaultExclusions;
    private List<Pending> _pending = new();
    private long _commitAt;
    private IntPtr _hwnd;
    private bool _ready;
    private bool _logged;
    private bool _classRegistered;

    public HeadsetVolume(Action<MediaAction, uint, bool> commit)
    {
        _commit = commit;
        _wndProc = WindowProc;
    }

    private const nint ReplayMark = 0x594D4721;

    internal static int InjectedInputSize => Marshal.SizeOf<Input>();

    public static bool IsReplay(UIntPtr extra) => extra == (UIntPtr)ReplayMark;

    public void Install()
    {
        try
        {
            var config = AppConfig.Load();
            _detections = config.HeadsetDetections;
            _exclusions = config.HeadsetExclusions;
            var instance = GetModuleHandle(null);
            var cls = new WndClass
            {
                lpfnWndProc = _wndProc,
                hInstance = instance,
                lpszClassName = "YtMusicMediaGuard.HeadsetVolume",
            };
            if (RegisterClass(ref cls) == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                Logger.Error("Headset volume window class was not registered (error " + Marshal.GetLastWin32Error() + ").");
                return;
            }

            _classRegistered = true;
            _hwnd = CreateWindowEx(0, cls.lpszClassName, "", 0, 0, 0, 0, 0, (IntPtr)HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Logger.Error("Headset volume window was not created (error " + Marshal.GetLastWin32Error() + ").");
                return;
            }

            var devices = new[]
            {
                new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevInputSink | RidevDevNotify, Target = _hwnd },
                new RawInputDevice { UsagePage = 0x0C, Usage = 0x01, Flags = RidevInputSink | RidevDevNotify, Target = _hwnd },
            };
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
            {
                Logger.Error("Headset volume source was not registered (error " + Marshal.GetLastWin32Error() + ").");
                return;
            }

            _ready = true;
        }
        catch (Exception ex)
        {
            Logger.Error("Headset volume source failed: " + ex.Message);
        }
    }

    public VolumeOrigin Classify(uint vk)
    {
        if (!_ready) return VolumeOrigin.Keyboard;
        uint size = 0;
        var header = HeaderSize;
        if (GetRawInputBuffer(IntPtr.Zero, ref size, header) == uint.MaxValue || size == 0)
            return VolumeOrigin.Unknown;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var count = GetRawInputBuffer(buffer, ref size, header);
            if (count == 0 || count == uint.MaxValue) return VolumeOrigin.Unknown;
            var matchedKeyboard = false;
            var matchedHeadsetKey = false;
            var matchedHeadsetHid = false;
            var cursor = buffer;
            for (uint i = 0; i < count; i++)
            {
                var type = (uint)Marshal.ReadInt32(cursor);
                var packet = Marshal.ReadInt32(cursor, 4);
                var device = Marshal.ReadIntPtr(cursor, 8);
                if (type == RimTypeKeyboard && packet >= (int)header + 8)
                {
                    var vkey = (uint)Marshal.ReadInt16(cursor, (int)header + 6);
                    if (vkey == vk)
                    {
                        matchedKeyboard = true;
                        if (DeviceIsHeadset(device)) matchedHeadsetKey = true;
                    }
                }
                else if (type == RimTypeHid && DeviceIsHeadset(device))
                {
                    matchedHeadsetHid = true;
                }

                if (packet <= 0) break;
                cursor += Align(packet);
            }

            if (matchedHeadsetKey || matchedHeadsetHid) return VolumeOrigin.Headset;
            if (matchedKeyboard) return VolumeOrigin.Keyboard;
            return VolumeOrigin.Unknown;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Defer(MediaAction action, uint vk, bool keyUp, bool passedToWindows)
    {
        _pending.Add(new Pending(Environment.TickCount64, action, vk, keyUp, passedToWindows));
        _commitAt = Environment.TickCount64 + PendingMs;
        if (_hwnd != IntPtr.Zero)
            SetTimer(_hwnd, (UIntPtr)1, 20, IntPtr.Zero);
    }

    public void ReleasePending()
    {
        if (_pending.Count == 0) return;
        foreach (var item in TakePending())
        {
            if (!item.PassedToWindows)
                Replay(item.Vk, item.KeyUp);
        }
    }

    public void Dispose()
    {
        _ready = false;
        TakePending();
        if (_hwnd != IntPtr.Zero)
        {
            KillTimer(_hwnd, (UIntPtr)1);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_classRegistered)
        {
            UnregisterClass("YtMusicMediaGuard.HeadsetVolume", GetModuleHandle(null));
            _classRegistered = false;
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == WmDeviceChange && wParam == (IntPtr)2)
                _headset.Remove(lParam);
            else if (message == WmInput)
                OnInput(lParam);
            else if (message == WmTimer)
                FlushAged();
        }
        catch (Exception ex)
        {
            Logger.Error("Headset volume message failed: " + ex.Message);
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void OnInput(IntPtr handle)
    {
        if (!TryRead(handle, out var type, out var device, out var vkey) || _pending.Count == 0)
            return;
        var headset = DeviceIsHeadset(device);
        if (headset && (type == RimTypeHid || (type == RimTypeKeyboard && IsMediaKey(vkey))))
        {
            ReleaseToWindows();
            return;
        }

        if (type == RimTypeKeyboard && IsMediaKey(vkey))
            CommitPending();
    }

    private void FlushAged()
    {
        if (_pending.Count == 0 || _commitAt == 0 || Environment.TickCount64 < _commitAt) return;
        CommitPending();
    }

    private void ReleaseToWindows()
    {
        if (_pending.Count == 0) return;
        NoteOnce();
        foreach (var item in TakePending())
        {
            if (!item.PassedToWindows)
                Replay(item.Vk, item.KeyUp);
        }
    }

    private static void Replay(uint vk, bool keyUp)
    {
        var input = new Input
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KeyInput
                {
                    wVk = (ushort)vk,
                    dwFlags = (keyUp ? 2u : 0u) | 1u,
                    dwExtraInfo = (IntPtr)ReplayMark,
                },
            },
        };
        var sent = SendInput(1, new[] { input }, InjectedInputSize);
        if (sent != 1)
            Logger.Error("Headset volume was not returned to Windows (error " + Marshal.GetLastWin32Error() + ").");
    }

    private void CommitPending()
    {
        foreach (var item in TakePending())
            _commit(item.Action, item.Vk, item.KeyUp);
    }

    private List<Pending> TakePending()
    {
        var items = _pending;
        _pending = new List<Pending>();
        _commitAt = 0;
        if (_hwnd != IntPtr.Zero) KillTimer(_hwnd, (UIntPtr)1);
        return items;
    }

    private void NoteOnce()
    {
        if (_logged) return;
        _logged = true;
        Logger.Info("Headset volume was left for Windows.");
    }

    private bool DeviceIsHeadset(IntPtr device)
    {
        if (device == IntPtr.Zero) return false;
        if (_headset.TryGetValue(device, out var known)) return known;
        var path = DevicePath(device) ?? "";
        if (VolumeHeadset.Matches(path, null, _detections, _exclusions))
        {
            _headset[device] = true;
            return true;
        }

        var product = path.Length == 0 ? null : ProductName(path);
        var headset = VolumeHeadset.Matches(path, product, _detections, _exclusions);
        _headset[device] = headset;
        return headset;
    }

    private static string? DevicePath(IntPtr device)
    {
        uint size = 0;
        if (GetRawInputDeviceInfo(device, RidiDeviceName, IntPtr.Zero, ref size) != 0 || size == 0)
            return null;
        var buffer = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            if (GetRawInputDeviceInfo(device, RidiDeviceName, buffer, ref size) == uint.MaxValue)
                return null;
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? ProductName(string path)
    {
        var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return null;
        try
        {
            var buffer = new byte[254];
            if (!HidD_GetProductString(handle, buffer, (uint)buffer.Length)) return null;
            var text = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
            return text.Length == 0 ? null : text;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool TryRead(IntPtr handle, out uint type, out IntPtr device, out uint vkey)
    {
        type = 0;
        device = IntPtr.Zero;
        vkey = 0;
        uint size = 0;
        var header = HeaderSize;
        if (GetRawInputData(handle, RidInput, IntPtr.Zero, ref size, header) != 0 || size == 0)
            return false;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(handle, RidInput, buffer, ref size, header) == uint.MaxValue)
                return false;
            type = (uint)Marshal.ReadInt32(buffer);
            device = Marshal.ReadIntPtr(buffer, 8);
            if (type == RimTypeKeyboard && size >= header + 8)
                vkey = (uint)Marshal.ReadInt16(buffer, (int)header + 6);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsMediaKey(uint vk) => vk is 0xAD or 0xAE or 0xAF or 0xB0 or 0xB1 or 0xB3;

    private static int Align(int size) => (size + 7) & ~7;

    private static uint HeaderSize => (uint)(IntPtr.Size == 8 ? 24 : 16);

    private readonly record struct Pending(long Tick, MediaAction Action, uint Vk, bool KeyUp, bool PassedToWindows);

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style;
        public WndProc lpfnWndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeyInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WndClass cls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputBuffer(IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint milliseconds, IntPtr procedure);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hwnd, UIntPtr id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(IntPtr device, byte[] buffer, uint length);
}

internal enum VolumeOrigin
{
    Keyboard,
    Headset,
    Unknown,
}
