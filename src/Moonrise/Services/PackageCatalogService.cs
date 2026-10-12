using System.Security.Cryptography;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class PackageCatalogService(JarMetadataParser parser)
{
    // Reviewed, shipped artifacts only. Never interpret catalog data as arbitrary local paths.
    public static IReadOnlyList<PackageManifest> LoadBundledCatalog() =>
    [
        Bundled("moonrise-cosmetics", "Moonrise Cosmetics", "1.3.2", PackageKind.JavaAgent,
            "Local Lunar cosmetics and outfit persistence; standalone Java agent for Minecraft 1.8.9.",
            "runtime/maintained-builds/moonrise-cosmetics-1.3.2.jar",
            "EFBAC1DC8F6B28EE1A13E294FF7DC03880C384306F1FFAB88738E3DCD1DC4354", "MIT; ASM BSD-3-Clause"),
        Bundled("veyra", "Veyra", "0.1.1-moonrise.1", PackageKind.WeaveMod,
            "Client mod for Weave 1.3.4 on Minecraft 1.8.9; ClickGUI: Enter.",
            "runtime/catalog/Veyra-0.1.1-moonrise.1.jar",
            "779760392BCA1F269D0EB325E78950249D355E94A09C90988108C3CCD94D95BD", "Distribution permission confirmed by owner")
    ];

    private static PackageManifest Bundled(string id, string name, string version, PackageKind kind,
        string summary, string path, string hash, string license) => new()
    {
        Id = id, Slug = id, Name = name, Type = kind, Summary = summary, Description = summary,
        CompatibilityLabel = "works-directly · Minecraft 1.8.9 (QA 2026-10-12)",
        Authors = [new("Moonrise")], License = new(license), SupportedMinecraftVersions = ["1.8.9"],
        InstallPolicy = PackageInstallPolicy.OneClick, RiskLevel = kind == PackageKind.JavaAgent
            ? PackageRiskLevel.Normal : PackageRiskLevel.ServerDependent,
        Releases = [new() { Version = version, PublishedAt = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero),
            Artifact = new() { DownloadMode = PackageDownloadMode.Bundled, AssetUrl = path,
                FileName = Path.GetFileName(path), Sha256 = hash, MinecraftVersions = ["1.8.9"] } }]
    };

    public static string ResolveBundledArtifact(PackageManifest package, PackageRelease release, string baseDirectory)
    {
        var approved = LoadBundledCatalog().SingleOrDefault(item => item.Id == package.Id);
        var artifact = approved?.LatestRelease?.Artifact;
        if (artifact is null || package.Type != approved!.Type || release.Version != approved.VersionLabel ||
            release.Artifact.AssetUrl != artifact.AssetUrl || release.Artifact.FileName != artifact.FileName ||
            release.Artifact.Sha256 != artifact.Sha256)
            throw new InvalidDataException("Unrecognized bundled catalog artifact.");
        return Path.Combine(baseDirectory, artifact.AssetUrl!.Replace('/', Path.DirectorySeparatorChar));
    }
    private const long MaximumJarSize = 512L * 1024 * 1024;

    public IReadOnlyList<PackageInfo> Load(string directory, PackageKind kind, ISet<string> disabled)
    {
        Directory.CreateDirectory(directory);
        var result = new List<PackageInfo>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.jar").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var package = Parse(path, kind);
            package.IsEnabled = !disabled.Contains(package.FileName);
            result.Add(package);
        }
        return result;
    }

    public string Import(string sourcePath, string directory, PackageKind kind)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("JAR file was not found.", source);
        if (!string.Equals(Path.GetExtension(source), ".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .jar files are supported.");
        var length = new FileInfo(source).Length;
        if (length <= 0 || length > MaximumJarSize)
            throw new InvalidDataException("JAR size must be between 1 byte and 512 MB.");
        _ = Parse(source, kind);

        Directory.CreateDirectory(directory);
        var hash = ComputeHash(source);
        foreach (var existing in Directory.EnumerateFiles(directory, "*.jar"))
        {
            if (string.Equals(hash, ComputeHash(existing), StringComparison.Ordinal)) return existing;
        }

        var destination = AvailablePath(directory, Path.GetFileNameWithoutExtension(source), ".jar");
        var temporary = destination + $".importing-{Guid.NewGuid():N}";
        try
        {
            File.Copy(source, temporary, false);
            File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Remove(PackageInfo package, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(package.FullPath);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only packages in a Moonrise user directory can be removed.");
        if (File.Exists(target)) File.Delete(target);
    }

    private PackageInfo Parse(string path, PackageKind kind) => kind == PackageKind.WeaveMod
        ? parser.ParseWeaveMod(path)
        : parser.ParseJavaAgent(path);

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string AvailablePath(string directory, string baseName, string extension)
    {
        var path = Path.Combine(directory, baseName + extension);
        for (var index = 2; File.Exists(path); index++) path = Path.Combine(directory, $"{baseName} ({index}){extension}");
        return path;
    }
}
