using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace YtMusicMediaGuard;

internal static class Stdio
{
    public static bool InputIsPipe()
    {
        var handle = GetStdHandle(-10);
        if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return false;
        return GetFileType(handle) == 3;
    }

    public static Stream OpenInput() => Open(-10, FileAccess.Read);

    public static Stream OpenOutput() => Open(-11, FileAccess.Write);

    private static Stream Open(int standardHandle, FileAccess access)
    {
        var handle = GetStdHandle(standardHandle);
        if (handle == IntPtr.Zero || handle == (IntPtr)(-1))
            throw new IOException("This process has no standard stream for native messaging.");
        return new FileStream(new SafeFileHandle(handle, ownsHandle: false), access, bufferSize: 16, isAsync: false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr hFile);
}
