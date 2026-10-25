using Microsoft.Win32;

namespace YtMusicMediaGuard;

internal static class StartupRegistration
{
    private const string ValueName = "YtMusicMediaGuard";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{AppPaths.ExecutablePath}\" --tray");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
