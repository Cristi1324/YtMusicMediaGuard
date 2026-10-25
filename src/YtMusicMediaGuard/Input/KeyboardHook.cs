using System.ComponentModel;
using System.Runtime.InteropServices;

namespace YtMusicMediaGuard;

internal sealed class KeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc;
    private readonly Control _invoker;
    private readonly HashSet<uint> _held = new();
        private IntPtr _hook;
        private uint _threadId;
        private Thread? _thread;
        private System.Threading.Timer? _rehook;
        private volatile bool _installed;

        public bool IsRunning => _installed && _thread?.IsAlive == true;

    public KeyboardHook(Control invoker)
    {
        _invoker = invoker;
        _proc = Callback;
    }

    public Func<bool>? IsActive { get; set; }
    public Action<MediaAction>? OnAction { get; set; }

    public void Start()
    {
        Exception? error = null;
        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            try
            {
                _threadId = GetCurrentThreadId();
                SetThreadPriority(GetCurrentThread(), ThreadPriorityTimeCritical);
                _hook = SetWindowsHookEx(WhKeyboardLl, _proc, ModuleHandle(), 0);
                if (_hook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
                _installed = true;
                ready.Set();
                while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.message == WmRehook)
                    {
                        Reinstall();
                        continue;
                    }

                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            catch (Exception ex)
            {
                error = ex;
                ready.Set();
            }
            finally
            {
                _installed = false;
                if (_hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
            }
        })
        {
            IsBackground = true,
            Name = "MediaKeyHook",
        };

        _thread.Start();
        ready.Wait();
        if (error is not null) throw error;
        _rehook = new System.Threading.Timer(_ =>
        {
            if (_threadId != 0)
                PostThreadMessage(_threadId, WmRehook, UIntPtr.Zero, IntPtr.Zero);
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _rehook?.Dispose();
        if (_threadId != 0)
            PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code < 0 || IsActive?.Invoke() != true)
            {
                if (_held.Count > 0) _held.Clear();
                return CallNextHookEx(_hook, code, wParam, lParam);
            }

            var data = Marshal.PtrToStructure<KbdLlHook>(lParam);
            var action = ActionFor(data.vkCode);
            if (action is null)
                return CallNextHookEx(_hook, code, wParam, lParam);

            // Play, next, and previous stay with Chrome unless a game or Remote Desktop is focused.
            // Those windows consume the key before Chrome's media session can see it.
            var capture = action is MediaAction.VolumeUp or MediaAction.VolumeDown or MediaAction.Mute
                || FocusDetector.OwnsTransport;
            if (!capture)
                return CallNextHookEx(_hook, code, wParam, lParam);

            var keyUp = (data.flags & LlkhfUp) != 0 || (uint)wParam is WmKeyUp or WmSysKeyUp;
            if (keyUp)
            {
                _held.Remove(data.vkCode);
            }
            else
            {
                var repeat = !_held.Add(data.vkCode);
                if (action is MediaAction.VolumeUp or MediaAction.VolumeDown or MediaAction.Mute)
                    VolumeGuard.NoteMediaKey();
                if (!repeat || action is MediaAction.VolumeUp or MediaAction.VolumeDown)
                    Raise(action.Value);
            }

            // Client keyboards arrive through Remote Desktop with LLKHF_INJECTED set.
            // Media keys are never injected by this app, so swallowing injected ones does not loop.
            return 1;
        }
        catch (Exception ex)
        {
            Logger.Error("Hook callback failed: " + ex.Message);
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
    }

    private void Raise(MediaAction action)
    {
        var handler = OnAction;
        if (handler is null) return;
        try
        {
            _invoker.BeginInvoke(handler, action);
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message);
        }
    }

    private static MediaAction? ActionFor(uint vk) => vk switch
    {
        0xB3 => MediaAction.PlayPause,
        0xB0 => MediaAction.Next,
        0xB1 => MediaAction.Previous,
        0xAF => MediaAction.VolumeUp,
        0xAE => MediaAction.VolumeDown,
        0xAD => MediaAction.Mute,
        _ => null,
    };

    private void Reinstall()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, ModuleHandle(), 0);
        _installed = _hook != IntPtr.Zero;
        if (!_installed)
            Logger.Error("The keyboard hook could not be reinstalled (error " + Marshal.GetLastWin32Error() + ").");
    }

    private static IntPtr ModuleHandle()
    {
        using var process = Process.GetCurrentProcess();
        var handle = GetModuleHandle(process.MainModule?.ModuleName);
        return handle == IntPtr.Zero ? GetModuleHandle(null) : handle;
    }

    private const int WhKeyboardLl = 13;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;
    private const uint WmRehook = 0x8001;
    private const int ThreadPriorityTimeCritical = 15;
    private const uint LlkhfUp = 0x80;

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHook
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
