using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class DeveloperPackageInspector
{
    public DeveloperInspection Inspect(string path, bool includeClassListing = false)
    {
        ManagedPackageInstaller.ValidateArchive(path);
        var fullPath = Path.GetFullPath(path);
        var kind = ManagedPackageInstaller.DetectType(fullPath);
        using var archive = ZipFile.OpenRead(fullPath);
        var manifest = ReadText(archive, "META-INF/MANIFEST.MF") ?? string.Empty;
        var weave = ReadText(archive, "weave.mod.json");
        var entryPoints = new List<string>();
        var mixins = new List<string>();
        if (weave is not null)
        {
            using var document = JsonDocument.Parse(weave);
            ReadArray(document.RootElement, "entryPoints", entryPoints);
            ReadArray(document.RootElement, "entrypoints", entryPoints);
            ReadArray(document.RootElement, "mixinConfigs", mixins);
        }
        else
        {
            foreach (var line in manifest.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith("Premain-Class:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Agent-Class:", StringComparison.OrdinalIgnoreCase))
                    entryPoints.Add(line[(line.IndexOf(':') + 1)..].Trim());
        }

        var classes = archive.Entries.Where(entry => entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.FullName[..^6].Replace('/', '.')).Order(StringComparer.Ordinal).ToArray();
        var hooks = classes.Where(name => name.Contains("hook", StringComparison.OrdinalIgnoreCase)).ToArray();
        var classVersion = archive.Entries.Where(entry => entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
            .Select(ReadClassVersion).Where(version => version.HasValue).Select(version => version!.Value).DefaultIfEmpty().Max();
        using var stream = File.OpenRead(fullPath);
        return new DeveloperInspection(
            Path.GetFileName(fullPath), kind, Convert.ToHexString(SHA256.HashData(stream)), new FileInfo(fullPath).Length,
            classVersion == 0 ? null : classVersion, manifest, weave, entryPoints.Distinct().ToArray(),
            mixins.Distinct().ToArray(), hooks, includeClassListing ? classes : []);
    }

    public PackageManifest CreateDraft(DeveloperInspection inspection)
    {
        var slug = Path.GetFileNameWithoutExtension(inspection.FileName).ToLowerInvariant();
        slug = string.Concat(slug.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')).Trim('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "package";
        return new PackageManifest
        {
            Id = slug,
            Slug = slug,
            Type = inspection.Kind,
            Name = Path.GetFileNameWithoutExtension(inspection.FileName),
            Summary = "Describe this package.",
            Description = "",
            Releases =
            [
                new PackageRelease
                {
                    Version = "1.0.0",
                    PublishedAt = DateTimeOffset.UtcNow,
                    Artifact = new PackageArtifact
                    {
                        DownloadMode = PackageDownloadMode.SourceOnly,
                        FileName = inspection.FileName,
                        FileSize = inspection.FileSize,
                        Sha256 = inspection.Sha256,
                        JavaVersion = inspection.JavaClassVersion
                    }
                }
            ]
        };
    }

    public string ExportDraft(PackageManifest draft) => JsonSerializer.Serialize(draft, CatalogManifestService.CreateOptions());

    private static string? ReadText(ZipArchive archive, string name)
    {
        var entry = archive.Entries.FirstOrDefault(item => string.Equals(item.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static void ReadArray(JsonElement root, string name, ICollection<string> target)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return;
        foreach (var item in value.EnumerateArray()) if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text) target.Add(text);
    }

    private static int? ReadClassVersion(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        Span<byte> header = stackalloc byte[8];
        if (stream.Read(header) != header.Length || header[0] != 0xCA || header[1] != 0xFE || header[2] != 0xBA || header[3] != 0xBE)
            return null;
        var major = (header[6] << 8) | header[7];
        return major >= 45 ? major - 44 : major;
    }
}
