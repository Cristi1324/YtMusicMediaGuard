using System.Security.Cryptography;
using System.Text.Json;

namespace YtMusicMediaGuard;

internal static class ExtensionIdentity
{
    public const string ExpectedId = "mbehpmabnicdenlcifcenanlcijcecgk";

    public static string ManifestPath()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "extension", "manifest.json"),
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && dir is not null; depth++)
        {
            candidates.Add(Path.Combine(dir.FullName, "extension", "manifest.json"));
            dir = dir.Parent;
        }

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
            throw new FileNotFoundException("The companion extension manifest was not found next to the app.");
        return found;
    }

    public static string PublicKey()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        if (!json.RootElement.TryGetProperty("key", out var key) || key.GetString() is not { Length: > 0 } value)
            throw new InvalidDataException("extension/manifest.json is missing its fixed public key.");
        return value;
    }

    public static string IdFromPublicKey(string base64Key)
    {
        var der = Convert.FromBase64String(base64Key);
        var hash = SHA256.HashData(der);
        var id = new char[32];
        for (var i = 0; i < 16; i++)
        {
            id[i * 2] = (char)('a' + ((hash[i] >> 4) & 0xF));
            id[i * 2 + 1] = (char)('a' + (hash[i] & 0xF));
        }

        return new string(id);
    }

    public static string CurrentId() => IdFromPublicKey(PublicKey());
}
