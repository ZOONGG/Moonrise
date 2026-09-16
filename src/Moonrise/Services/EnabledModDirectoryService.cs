using System.Security.Cryptography;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class EnabledModDirectoryService(JarMetadataParser parser)
{
    public string Create(
        string launchDirectory,
        IEnumerable<PackageInfo> enabledMods,
        bool legacyWeaveLayout = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchDirectory);
        var enabledDirectory = legacyWeaveLayout
            ? Path.Combine(Path.GetFullPath(launchDirectory), ".weave", "mods")
            : Path.Combine(Path.GetFullPath(launchDirectory), "enabled-mods");
        Directory.CreateDirectory(enabledDirectory);

        var mods = enabledMods.ToArray();
        var duplicateNames = mods.GroupBy(
                mod => mod.FileName,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            var sourcePath = Path.GetFullPath(mod.FullPath);
            var parsed = parser.ParseWeaveMod(sourcePath);
            if (!string.Equals(parsed.Sha256, mod.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{mod.FileName}: stored package hash changed before launch preparation.");

            var destinationName = duplicateNames.Contains(mod.FileName)
                ? $"{mod.Sha256.ToLowerInvariant()}.jar"
                : mod.FileName;
            var destinationPath = Path.Combine(enabledDirectory, destinationName);
            if (File.Exists(destinationPath))
                throw new IOException($"{mod.OriginalFileName}: duplicate enabled-mod destination.");

            File.Copy(sourcePath, destinationPath, overwrite: false);
            var copiedHash = ComputeSha256(destinationPath);
            var sourceHashAfterCopy = ComputeSha256(sourcePath);
            if (!string.Equals(copiedHash, parsed.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(sourceHashAfterCopy, parsed.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{mod.OriginalFileName}: byte-for-byte launch copy verification failed.");
            }
        }

        return enabledDirectory;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

}
