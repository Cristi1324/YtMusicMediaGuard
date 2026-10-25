using System.Windows.Forms;

namespace YtMusicMediaGuard;

internal enum MediaAction
{
    PlayPause,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    Mute,
}

internal sealed class Chord
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public string Key { get; set; } = "P";

    public string Format()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }

    public static Chord Parse(string text)
    {
        var chord = new Chord();
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    chord.Ctrl = true;
                    break;
                case "alt":
                    chord.Alt = true;
                    break;
                case "shift":
                    chord.Shift = true;
                    break;
                case "win":
                case "windows":
                    chord.Win = true;
                    break;
                default:
                    key = CanonicalKey(part);
                    break;
            }
        }

        if (key is null) throw new FormatException($"Shortcut \"{text}\" has no key.");
        chord.Key = key;
        return chord;
    }

    public static string? NameFromKey(Keys key)
    {
        if (key is >= Keys.A and <= Keys.Z) return key.ToString();
        if (key is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9) return ((char)('0' + (key - Keys.NumPad0))).ToString();
        if (key is >= Keys.F1 and <= Keys.F24) return key.ToString();
        return key switch
        {
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.Space => "Space",
            Keys.Tab => "Tab",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp or Keys.Prior => "PageUp",
            Keys.PageDown or Keys.Next => "PageDown",
            Keys.Insert => "Insert",
            Keys.Delete => "Delete",
            _ => null,
        };
    }

    public ushort VirtualKey => ToVirtualKey(Key);

    public static ushort ToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }

        if (key.Length is 2 or 3 && key[0] is 'F' or 'f' && int.TryParse(key[1..], out var function) && function is >= 1 and <= 24)
            return (ushort)(0x70 + function - 1);

        return key.ToLowerInvariant() switch
        {
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "space" => 0x20,
            "tab" => 0x09,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "insert" => 0x2D,
            "delete" => 0x2E,
            _ => throw new FormatException($"Unsupported shortcut key \"{key}\"."),
        };
    }

    public static bool IsExtended(ushort virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C;

    private static string CanonicalKey(string key)
    {
        _ = ToVirtualKey(key);
        if (key.Length == 1) return key.ToUpperInvariant();
        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f')) return "F" + key[1..];
        return key.ToLowerInvariant() switch
        {
            "up" => "Up",
            "down" => "Down",
            "left" => "Left",
            "right" => "Right",
            "space" => "Space",
            "tab" => "Tab",
            "home" => "Home",
            "end" => "End",
            "pageup" => "PageUp",
            "pagedown" => "PageDown",
            "insert" => "Insert",
            "delete" => "Delete",
            _ => key,
        };
    }
}
