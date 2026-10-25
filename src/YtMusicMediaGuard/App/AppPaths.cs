namespace YtMusicMediaGuard;

internal static class AppPaths
{
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YtMusicMediaGuard");

    public static string ConfigFile => Path.Combine(Directory, "config.json");

    public static string HostManifest => Path.Combine(Directory, "com.ytmusic.mediaguard.json");

    public static string IntroducedMarker => Path.Combine(Directory, ".introduced");

    public static string ExecutablePath =>
        Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "YtMusicMediaGuard.exe");
}
