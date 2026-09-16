using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class ManagedPackageInstaller(HttpClient httpClient, JarMetadataParser parser)
{
    private const long MaximumPackageSize = 512L * 1024 * 1024;

    public async Task<string> InstallAsync(
        PackageManifest package,
        PackageRelease release,
        string managedRoot,
        IEnumerable<PackageInfo> installed,
        CancellationToken cancellationToken = default)
    {
        var artifact = release.Artifact;
        if (!package.CanInstall || artifact.AssetUrl is null || artifact.FileName is null || artifact.Sha256 is null)
            throw new InvalidOperationException("This package does not provide an installable artifact.");
        var uri = new Uri(artifact.AssetUrl, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Package downloads must use HTTPS.");
        ResolveDependencies(package, release, installed);

        var kindDirectory = Path.Combine(
            Path.GetFullPath(managedRoot),
            package.Type == PackageKind.JavaAgent ? "agents" : "weave");
        Directory.CreateDirectory(kindDirectory);
        var destination = SafeDestination(kindDirectory, artifact.FileName);
        var temporary = Path.Combine(kindDirectory, $".download-{Guid.NewGuid():N}.tmp");
        var rollback = destination + $".rollback-{Guid.NewGuid():N}";
        try
        {
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumPackageSize)
                throw new InvalidDataException("The package exceeds the 512 MB limit.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(output, cancellationToken);
                if (output.Length <= 0 || output.Length > MaximumPackageSize)
                    throw new InvalidDataException("The downloaded package size is invalid.");
            }
            if (artifact.FileSize is { } expectedSize && new FileInfo(temporary).Length != expectedSize)
                throw new InvalidDataException("The downloaded package size does not match the catalog.");
            if (!string.Equals(ComputeHash(temporary), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("The downloaded package SHA-256 does not match the catalog.");
            ValidateArchive(temporary);
            var detected = DetectType(temporary);
            if (detected != package.Type) throw new InvalidDataException("The downloaded package type does not match the catalog.");
            _ = detected == PackageKind.WeaveMod ? parser.ParseWeaveMod(temporary) : parser.ParseJavaAgent(temporary);

            if (File.Exists(destination)) File.Move(destination, rollback);
            try
            {
                File.Move(temporary, destination);
                if (File.Exists(rollback)) File.Delete(rollback);
            }
            catch
            {
                if (File.Exists(destination)) File.Delete(destination);
                if (File.Exists(rollback)) File.Move(rollback, destination);
                throw;
            }
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(rollback) && !File.Exists(destination)) File.Move(rollback, destination);
        }
    }

    public void Uninstall(string packagePath, string managedRoot)
    {
        var root = Path.GetFullPath(managedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(packagePath);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Manually imported packages are not removed by the managed installer.");
        if (File.Exists(target)) File.Delete(target);
    }

    public static PackageKind DetectType(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Any(entry => string.Equals(entry.FullName, "weave.mod.json", StringComparison.OrdinalIgnoreCase)))
            return PackageKind.WeaveMod;
        var manifest = archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName, "META-INF/MANIFEST.MF", StringComparison.OrdinalIgnoreCase));
        if (manifest is not null)
        {
            using var reader = new StreamReader(manifest.Open());
            var text = reader.ReadToEnd();
            if (text.Contains("Premain-Class:", StringComparison.OrdinalIgnoreCase) || text.Contains("Agent-Class:", StringComparison.OrdinalIgnoreCase))
                return PackageKind.JavaAgent;
        }
        throw new InvalidDataException("The JAR is neither a Weave mod nor a Java agent.");
    }

    public static void ValidateArchive(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count == 0) throw new InvalidDataException("The JAR is empty.");
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Split('/').Any(part => part == ".."))
                throw new InvalidDataException("The JAR contains an unsafe traversal path.");
        }
    }

    private static void ResolveDependencies(PackageManifest package, PackageRelease release, IEnumerable<PackageInfo> installed)
    {
        var identifiers = installed.Select(item => item.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dependencies = package.Dependencies.Concat(release.Dependencies).Where(item => !item.Optional);
        var missing = dependencies.FirstOrDefault(item => !identifiers.Contains(item.PackageId));
        if (missing is not null) throw new InvalidOperationException($"Required package '{missing.PackageId}' is not installed.");
        var conflict = package.Conflicts.Concat(release.Conflicts).FirstOrDefault(item => identifiers.Contains(item.PackageId));
        if (conflict is not null) throw new InvalidOperationException($"Installed package '{conflict.PackageId}' conflicts with this package.");
    }

    private static string SafeDestination(string root, string fileName)
    {
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) || !fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The catalog artifact file name is unsafe.");
        var destination = Path.GetFullPath(Path.Combine(root, fileName));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The package path escapes managed storage.");
        return destination;
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
