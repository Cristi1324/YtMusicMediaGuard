using System.Text.Json;
using System.Text.Json.Serialization;

namespace YtMusicMediaGuard;

internal sealed class BridgeMessage
{
    public string? Type { get; set; }
    public string? ProfileId { get; set; }
    public int OpenTabs { get; set; }
    public bool Audible { get; set; }
    public double LastActive { get; set; }
    public string? Command { get; set; }
    public bool Armed { get; set; }
}

internal static class MediaCommands
{
    public static string Name(MediaAction action) => action switch
    {
        MediaAction.PlayPause => "playPause",
        MediaAction.Next => "next",
        MediaAction.Previous => "previous",
        MediaAction.VolumeUp => "volumeUp",
        MediaAction.VolumeDown => "volumeDown",
        MediaAction.Mute => "mute",
        _ => "playPause",
    };

    public static bool TryParse(string? command, out MediaAction action)
    {
        switch (command)
        {
            case "playPause":
                action = MediaAction.PlayPause;
                return true;
            case "next":
                action = MediaAction.Next;
                return true;
            case "previous":
                action = MediaAction.Previous;
                return true;
            case "volumeUp":
                action = MediaAction.VolumeUp;
                return true;
            case "volumeDown":
                action = MediaAction.VolumeDown;
                return true;
            case "mute":
                action = MediaAction.Mute;
                return true;
            default:
                action = default;
                return false;
        }
    }
}

internal static class BridgeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
