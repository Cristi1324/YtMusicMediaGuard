using System.Text.Json;

namespace YtMusicMediaGuard;

internal sealed class AppConfig
{
    public bool StartWithWindows { get; set; } = true;
    public string PlayPause { get; set; } = "Ctrl+Alt+Shift+P";
    public string Next { get; set; } = "Ctrl+Alt+Shift+N";
    public string Previous { get; set; } = "Ctrl+Alt+Shift+B";
    public string VolumeUp { get; set; } = "Ctrl+Alt+Shift+Up";
    public string VolumeDown { get; set; } = "Ctrl+Alt+Shift+Down";
    public string Mute { get; set; } = "Ctrl+Alt+Shift+M";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string TextFor(MediaAction action) => action switch
    {
        MediaAction.PlayPause => PlayPause,
        MediaAction.Next => Next,
        MediaAction.Previous => Previous,
        MediaAction.VolumeUp => VolumeUp,
        MediaAction.VolumeDown => VolumeDown,
        MediaAction.Mute => Mute,
        _ => PlayPause,
    };

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ConfigFile))
            {
                var created = new AppConfig();
                created.Save();
                return created;
            }

            var json = File.ReadAllText(AppPaths.ConfigFile);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            loaded.Normalize();
            return loaded;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read config, using defaults. " + ex.Message);
            return new AppConfig();
        }
    }

    public void Normalize()
    {
        PlayPause = Valid(PlayPause, "Ctrl+Alt+Shift+P");
        Next = Valid(Next, "Ctrl+Alt+Shift+N");
        Previous = Valid(Previous, "Ctrl+Alt+Shift+B");
        VolumeUp = Valid(VolumeUp, "Ctrl+Alt+Shift+Up");
        VolumeDown = Valid(VolumeDown, "Ctrl+Alt+Shift+Down");
        Mute = Valid(Mute, "Ctrl+Alt+Shift+M");
    }

    private static string Valid(string? text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        try
        {
            return Chord.Parse(text).Format();
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Directory);
        File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(this, Options));
    }
}
