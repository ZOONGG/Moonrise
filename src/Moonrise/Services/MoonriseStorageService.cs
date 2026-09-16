using Moonrise.Infrastructure;

namespace Moonrise.Services;

public sealed record TemporaryCleanupResult(int FilesDeleted, int DirectoriesDeleted, long BytesFreed);

public sealed class MoonriseStorageService
{
    private readonly AppPaths _paths;

    public MoonriseStorageService(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public long CalculatePackageStorageSize()
    {
        if (!Directory.Exists(_paths.PackagesDirectory))
            return 0;
        return EnumerateFilesSafe(_paths.PackagesDirectory)
            .Sum(path =>
            {
                try { return new FileInfo(path).Length; }
                catch { return 0L; }
            });
    }

    public TemporaryCleanupResult ClearTemporaryFiles(DateTimeOffset now)
    {
        var filesDeleted = 0;
        var directoriesDeleted = 0;
        var bytesFreed = 0L;

        if (Directory.Exists(_paths.TempDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(
                         _paths.TempDirectory,
                         "launch-*",
                         SearchOption.TopDirectoryOnly))
            {
                var info = new DirectoryInfo(directory);
                if (info.LastWriteTimeUtc >= now.UtcDateTime.AddHours(-1))
                    continue;
                var full = Path.GetFullPath(directory);
                EnsureInside(full, _paths.TempDirectory);
                bytesFreed += CalculateSizeSafe(full);
                Directory.Delete(full, recursive: true);
                directoriesDeleted++;
            }
        }

        foreach (var category in new[]
                 {
                     _paths.WeavePackagesDirectory,
                     _paths.AgentPackagesDirectory,
                     _paths.UnclassifiedPackagesDirectory
                 })
        {
            DeleteMatchingFiles(
                category,
                ["*.importing", "*.migrating"],
                now.AddHours(-1),
                ref filesDeleted,
                ref bytesFreed);
        }
        DeleteMatchingFiles(
            _paths.UpdateDirectory,
            ["*.partial", "*.download", "*.tmp"],
            now.AddDays(-1),
            ref filesDeleted,
            ref bytesFreed,
            recursive: true);

        if (Directory.Exists(_paths.CrashesDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(
                         _paths.CrashesDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var info = new DirectoryInfo(directory);
                if (info.LastWriteTimeUtc >= now.UtcDateTime.AddDays(-30))
                    continue;
                EnsureInside(directory, _paths.CrashesDirectory);
                bytesFreed += CalculateSizeSafe(directory);
                Directory.Delete(directory, recursive: true);
                directoriesDeleted++;
            }
        }

        return new TemporaryCleanupResult(filesDeleted, directoriesDeleted, bytesFreed);
    }

    private static void DeleteMatchingFiles(
        string root,
        IReadOnlyList<string> patterns,
        DateTimeOffset olderThan,
        ref int filesDeleted,
        ref long bytesFreed,
        bool recursive = false)
    {
        if (!Directory.Exists(root))
            return;
        foreach (var pattern in patterns)
        {
            foreach (var path in Directory.EnumerateFiles(
                         root,
                         pattern,
                         recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc >= olderThan.UtcDateTime)
                    continue;
                EnsureInside(path, root);
                bytesFreed += info.Length;
                info.Delete();
                filesDeleted++;
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    private static long CalculateSizeSafe(string root) =>
        EnumerateFilesSafe(root).Sum(path =>
        {
            try { return new FileInfo(path).Length; }
            catch { return 0L; }
        });

    private static void EnsureInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clean outside the selected Moonrise temporary root.");
    }
}
