using System.Security.Cryptography;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class PackageCatalogService(JarMetadataParser parser)
{
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
