using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace YtMusicMediaGuard;

internal static class ChromeRegistration
{
    private const string HostName = "com.ytmusic.mediaguard";
    private const string RegistryPath = @"Software\Google\Chrome\NativeMessagingHosts\" + HostName;

    public static void Register()
    {
        var id = ExtensionIdentity.CurrentId();
        var manifest = new
        {
            name = HostName,
            description = "YouTube Music media key guard",
            path = AppPaths.ExecutablePath,
            type = "stdio",
            allowed_origins = new[] { $"chrome-extension://{id}/" },
        };

        Directory.CreateDirectory(AppPaths.Directory);
        File.WriteAllText(AppPaths.HostManifest, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("", AppPaths.HostManifest);
        Logger.Info($"Registered native host for extension {id}.");
    }

    public static void OpenShortcutPage()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        };

        var chrome = candidates.FirstOrDefault(File.Exists);
        if (chrome is null)
        {
            MessageBox.Show(
                "Chrome was not found. Open chrome://extensions/shortcuts and set the Tweaks for YouTube global shortcuts there.",
                "YouTube Music media key guard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = chrome,
            Arguments = "chrome://extensions/shortcuts",
            UseShellExecute = true,
        });
    }
}
