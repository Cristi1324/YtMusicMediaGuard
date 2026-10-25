namespace YtMusicMediaGuard;

internal static class Logger
{
    private static readonly string LogPath = Path.Combine(AppPaths.Directory, "guard.log");
    private static readonly object Gate = new();

    public static void Info(string message)
    {
#if DEBUG
        Write(message);
#endif
    }

    public static void Error(string message) => Write(message);

    private static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Directory);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(LogPath, line);
                var info = new FileInfo(LogPath);
                if (info.Length <= 200_000) return;
                var text = File.ReadAllText(LogPath);
                var keep = Math.Min(text.Length, 100_000);
                File.WriteAllText(LogPath, text[^keep..]);
            }
        }
        catch
        {
            // Logging must not take down the hook or the native host.
        }
    }
}
