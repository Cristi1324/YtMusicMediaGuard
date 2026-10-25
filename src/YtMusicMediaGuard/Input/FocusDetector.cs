using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace YtMusicMediaGuard;

internal static class FocusDetector
{
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "SearchHost",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "TextInputHost",
        "SystemSettings",
        "LockApp",
    };

    private static readonly object Gate = new();
    private static System.Threading.Timer? _timer;
    private static HashSet<string> _games = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _gamesLoadedUtc = DateTime.MinValue;
    private static string? _reason;
    private static uint _loggedElevatedPid;
    private static volatile bool _ownsTransport;
    private static volatile bool _foregroundElevated;

    public static bool OwnsTransport => _ownsTransport;
    public static bool ForegroundElevated => _foregroundElevated;

    public static void Start()
    {
        Refresh();
        _timer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400));
    }

    public static void Stop() => _timer?.Dispose();

    internal static bool CoversScreen(int left, int top, int right, int bottom, int monitorLeft, int monitorTop, int monitorRight, int monitorBottom) =>
        left <= monitorLeft + 2
        && top <= monitorTop + 2
        && right >= monitorRight - 2
        && bottom >= monitorBottom - 2;

    internal static bool IsListedGame(string? path, IReadOnlySet<string> games)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (games.Contains(path)) return true;
        var file = Path.GetFileName(path);
        return !string.IsNullOrEmpty(file) && games.Contains(file);
    }

    private static void Refresh()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || hwnd == GetDesktopWindow())
            {
                _foregroundElevated = false;
                SetOwns(false, null);
                return;
            }

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            var path = pid == 0 ? null : ImagePath(pid);
            var name = ProcessName(pid, path);
            if (name is not null && ShellProcesses.Contains(name))
            {
                _foregroundElevated = false;
                SetOwns(false, null);
                return;
            }

            _foregroundElevated = pid != 0 && IsElevated(pid);

            if (RdpDetector.IsClientProcess(name))
            {
                SetOwns(true, "Remote Desktop");
                return;
            }

            var game = IsExclusiveFullscreen() || CoversMonitor(hwnd) || IsListedGame(path, Games());
            if (game && _foregroundElevated && pid != _loggedElevatedPid && !CurrentIsElevated())
            {
                _loggedElevatedPid = pid;
                Logger.Error("The focused game is running as administrator. Allow the permission prompt so its media keys reach YouTube Music.");
            }

            SetOwns(game, game ? "a game" : null);
        }
        catch (Exception ex)
        {
            Logger.Error("Focus check failed: " + ex.Message);
        }
    }

    private static void SetOwns(bool owns, string? reason)
    {
        if (owns == _ownsTransport && reason == _reason) return;
        _ownsTransport = owns;
        _reason = reason;
        if (owns && reason is not null)
            Logger.Info($"Focused {reason}; play, next, and previous go to YouTube Music.");
    }

    private static HashSet<string> Games()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _gamesLoadedUtc < TimeSpan.FromSeconds(30)) return _games;
            _games = LoadGames();
            _gamesLoadedUtc = DateTime.UtcNow;
            return _games;
        }
    }

    private static HashSet<string> LoadGames()
    {
        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var children = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children");
        if (children is null) return games;

        foreach (var name in children.GetSubKeyNames())
        {
            using var child = children.OpenSubKey(name);
            if (child is null) continue;
            foreach (var valueName in new[] { "MatchedExeFullPath", "ExePath" })
            {
                var path = ReadPath(child.GetValue(valueName));
                if (path is null) continue;
                games.Add(path);
                var file = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(file)) games.Add(file);
            }
        }

        return games;
    }

    private static string? ReadPath(object? raw)
    {
        var text = raw switch
        {
            string value => value,
            byte[] bytes when bytes.Length >= 2 => Encoding.Unicode.GetString(bytes),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().TrimEnd('\0');
        try
        {
            return Path.GetFullPath(text);
        }
        catch (Exception)
        {
            return text;
        }
    }

    private static string? ProcessName(uint pid, string? path)
    {
        if (!string.IsNullOrEmpty(path)) return Path.GetFileNameWithoutExtension(path);
        if (pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool CoversMonitor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var window)) return false;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        var screen = info.rcMonitor;
        return CoversScreen(window.Left, window.Top, window.Right, window.Bottom, screen.Left, screen.Top, screen.Right, screen.Bottom);
    }

    private static bool CurrentIsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static bool IsExclusiveFullscreen()
    {
        return SHQueryUserNotificationState(out var state) == 0 && state == QueryRunningD3dFullScreen;
    }

    private static string? ImagePath(uint pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool IsElevated(uint pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(handle, TokenQuery, out var token)) return false;
            try
            {
                return GetTokenInformation(token, TokenElevationClass, out var elevation, Marshal.SizeOf<TokenElevation>(), out _)
                    && elevation.TokenIsElevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const uint MonitorDefaultToNearest = 2;
    private const int QueryRunningD3dFullScreen = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public uint TokenIsElevated;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        out TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}
