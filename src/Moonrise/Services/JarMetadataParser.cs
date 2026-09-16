using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class JarMetadataParser
{
    public PackageInfo ParseWeaveMod(string jarPath)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        var entry = archive.Entries.FirstOrDefault(item =>
            string.Equals(item.FullName, "weave.mod.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"{Path.GetFileName(jarPath)} does not contain weave.mod.json.");
        using var reader = new StreamReader(entry.Open());
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("weave.mod.json must contain a JSON object.");
        }

        var entrypoints = ReadStringArray(root, root.TryGetProperty("entryPoints", out _) ? "entryPoints" : "entrypoints");
        var path = Path.GetFullPath(jarPath);
        return new PackageInfo
        {
            PackageId = "sha256:" + ComputeHash(path).ToLowerInvariant(),
            FileName = Path.GetFileName(path),
            OriginalFileName = Path.GetFileName(path),
            DisplayName = ReadString(root, "name") ?? Path.GetFileNameWithoutExtension(path),
            Identifier = ReadString(root, "modId") ?? Path.GetFileNameWithoutExtension(path),
            Version = ReadString(root, "version") ?? "—",
            Entrypoint = entrypoints.FirstOrDefault() ?? "Weave entrypoint",
            FullPath = path,
            Kind = PackageKind.WeaveMod,
            Size = new FileInfo(path).Length,
            Sha256 = ComputeHash(path),
            CompatibleGameVersions = ReadCompatibleGameVersions(root),
            Conflicts = ReadStringArray(root, "conflicts"),
            Requires = ReadDependencies(root)
        };
    }

    public PackageInfo ParseJavaAgent(string jarPath)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        var entry = archive.Entries.FirstOrDefault(item =>
            string.Equals(item.FullName, "META-INF/MANIFEST.MF", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("META-INF/MANIFEST.MF is missing.");
        using var reader = new StreamReader(entry.Open());
        var manifest = ParseManifest(reader.ReadToEnd());
        var entrypoint = GetManifestValue(manifest, "Premain-Class") ?? GetManifestValue(manifest, "Agent-Class")
            ?? throw new InvalidDataException("The JAR has neither Premain-Class nor Agent-Class.");
        var path = Path.GetFullPath(jarPath);
        return new PackageInfo
        {
            PackageId = "sha256:" + ComputeHash(path).ToLowerInvariant(),
            FileName = Path.GetFileName(path),
            OriginalFileName = Path.GetFileName(path),
            DisplayName = GetManifestValue(manifest, "Implementation-Title") ?? Path.GetFileNameWithoutExtension(path),
            Identifier = entrypoint,
            Version = GetManifestValue(manifest, "Implementation-Version") ?? "—",
            Entrypoint = entrypoint,
            FullPath = path,
            Kind = PackageKind.JavaAgent,
            Size = new FileInfo(path).Length,
            Sha256 = ComputeHash(path),
            CompatibleGameVersions = SplitManifestList(GetManifestValue(manifest, "Moonrise-Minecraft-Versions")),
            Conflicts = SplitManifestList(GetManifestValue(manifest, "Moonrise-Conflicts")),
            Requires = SplitManifestList(GetManifestValue(manifest, "Moonrise-Requires"))
        };
    }

    private static Dictionary<string, string> ParseManifest(string raw)
    {
        var unfolded = raw.Replace("\r\n ", string.Empty, StringComparison.Ordinal)
            .Replace("\n ", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in unfolded.Split('\n'))
        {
            // Named manifest sections may contain dependency versions. Package
            // metadata belongs to the main section before the first blank line.
            if (line.Length == 0) break;
            var separator = line.IndexOf(':');
            if (separator > 0) result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static string? GetManifestValue(IReadOnlyDictionary<string, string> manifest, string key) =>
        manifest.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"{property} must be an array.");
        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
    }

    private static IReadOnlyList<string> ReadDependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var value)) return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().Select(item => item.Name).ToArray(),
            _ => throw new InvalidDataException("dependencies must be an array or object.")
        };
    }

    private static IReadOnlyList<string> ReadCompatibleGameVersions(JsonElement root)
    {
        var versions = ReadStringArray(root, "minecraftVersions");
        if (versions.Count > 0) return versions;
        var compiledFor = ReadString(root, "compiledFor");
        return string.IsNullOrWhiteSpace(compiledFor) ? [] : [compiledFor];
    }

    private static IReadOnlyList<string> SplitManifestList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
