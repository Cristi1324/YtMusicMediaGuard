using System.Runtime.InteropServices;

namespace YtMusicMediaGuard;

internal static class RdpDetector
{
    private static readonly HashSet<string> Clients = new(StringComparer.OrdinalIgnoreCase)
    {
        "mstsc",
        "msrdc",
        "vmconnect",
    };

    public static bool IsClientProcess(string? processName) =>
        processName is not null && Clients.Contains(processName);

    public static bool IsForegroundClient()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return false;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return IsClientProcess(process.ProcessName);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
