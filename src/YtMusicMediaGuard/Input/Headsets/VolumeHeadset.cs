namespace YtMusicMediaGuard;

internal static class VolumeHeadset
{
    public static readonly string[] DefaultDetections =
    {
        "PID_0ABA",
        "PID_0AF7",
        "PRO X Wireless",
        "PRO X 2 LIGHTSPEED",
    };

    public static readonly string[] DefaultExclusions =
    {
        "Keyboard",
        "Mouse",
        "Superlight",
    };

    public static bool Matches(string devicePath, string? product) =>
        Matches(devicePath, product, DefaultDetections, DefaultExclusions);

    public static bool Matches(string devicePath, string? product, IReadOnlyList<string> detections, IReadOnlyList<string> exclusions)
    {
        if (ContainsAny(product, exclusions)) return false;
        foreach (var needle in detections)
        {
            if (string.IsNullOrWhiteSpace(needle)) continue;
            if (devicePath.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
            if (product is not null && product.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool ContainsAny(string? text, IReadOnlyList<string> needles)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var needle in needles)
        {
            if (!string.IsNullOrWhiteSpace(needle) && text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
