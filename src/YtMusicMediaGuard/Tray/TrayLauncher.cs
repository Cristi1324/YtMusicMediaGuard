using System.Runtime.InteropServices;
using System.Text;

namespace YtMusicMediaGuard;

internal static class TrayLauncher
{
    public static void Start()
    {
        var exe = AppPaths.ExecutablePath;
        if (TryCreateProcess(exe)) return;

        try
        {
            using var processClass = new System.Management.ManagementClass("Win32_Process");
            using var parameters = processClass.GetMethodParameters("Create");
            parameters["CommandLine"] = $"\"{exe}\" --tray";
            using var result = processClass.InvokeMethod("Create", parameters, null);
            var code = result is null ? uint.MaxValue : (uint)(result["ReturnValue"] ?? uint.MaxValue);
            if (code == 0)
            {
                Logger.Info("Started the tray app through WMI.");
                return;
            }

            Logger.Error($"WMI could not start the tray app (code {code}).");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not start the tray app. " + ex.Message);
        }
    }

    private static bool TryCreateProcess(string exe)
    {
        var command = new StringBuilder($"\"{exe}\" --tray", 32_768);
        var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        const uint flags = 0x01000000 | 0x00000008 | 0x08000000;
        if (!CreateProcess(exe, command, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, null, ref startup, out var info))
        {
            Logger.Info($"CreateProcess could not start the tray app (error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        CloseHandle(info.hThread);
        CloseHandle(info.hProcess);
        Logger.Info("Started the tray app outside the Chrome host job.");
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
