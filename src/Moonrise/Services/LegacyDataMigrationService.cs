using Moonrise.Infrastructure;

namespace Moonrise.Services;

public sealed record MigrationResult(int CopiedFiles, int SkippedFiles, IReadOnlyList<string> Warnings);

public sealed class LegacyDataMigrationService
{
    private static readonly (string Source, Func<AppPaths, string> Destination)[] DirectoryMappings =
    [
        ("user-mods", paths => paths.UserModsDirectory),
        ("user-agents", paths => paths.UserAgentsDirectory),
        ("managed-packages", paths => paths.ManagedPackagesDirectory),
        ("profiles", paths => paths.ProfilesDirectory),
        ("plugins", paths => paths.PluginsDirectory),
        ("language-packs", paths => paths.LanguagePacksDirectory),
        ("themes", paths => paths.ThemesDirectory),
        (Path.Combine("catalog", "cache"), paths => paths.CatalogCacheDirectory),
        ("logs", paths => paths.LogsDirectory),
        ("crash-reports", paths => paths.CrashReportsDirectory)
    ];

    private static readonly (string Source, Func<AppPaths, string> Destination)[] FileMappings =
    [
        ("moonrise-settings.json", paths => paths.SettingsPath)
    ];

    public MigrationResult Migrate(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureUserDirectories();
        if (File.Exists(paths.MigrationMarkerPath))
            return new MigrationResult(0, 0, []);

        var legacyRoot = Path.GetFullPath(paths.InstallationDirectory);
        var dataRoot = Path.GetFullPath(paths.RootDirectory);
        var copied = 0;
        var skipped = 0;
        var warnings = new List<string>();

        foreach (var (sourceRelativePath, destinationFactory) in DirectoryMappings)
        {
            var source = Path.Combine(legacyRoot, sourceRelativePath);
            var destination = destinationFactory(paths);
            if (PathsEqual(source, destination) || !Directory.Exists(source))
                continue;

            CopyDirectory(source, destination, ref copied, ref skipped, warnings);
        }

        foreach (var (sourceRelativePath, destinationFactory) in FileMappings)
        {
            var source = Path.Combine(legacyRoot, sourceRelativePath);
            var destination = destinationFactory(paths);
            if (PathsEqual(source, destination) || !File.Exists(source))
                continue;

            CopyFile(source, destination, ref copied, ref skipped, warnings);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(paths.MigrationMarkerPath)!);
        File.WriteAllText(
            paths.MigrationMarkerPath,
            $"Legacy Moonrise data migration completed at {DateTimeOffset.UtcNow:O}.{Environment.NewLine}" +
            $"Source: {legacyRoot}{Environment.NewLine}Destination: {dataRoot}{Environment.NewLine}");
        return new MigrationResult(copied, skipped, warnings);
    }

    private static void CopyDirectory(
        string source,
        string destination,
        ref int copied,
        ref int skipped,
        ICollection<string> warnings)
    {
        var sourceRoot = Path.GetFullPath(source);
        var destinationRoot = Path.GetFullPath(destination);
        Directory.CreateDirectory(destinationRoot);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.GetFullPath(Path.Combine(destinationRoot, relative));
            if (!IsWithin(target, destinationRoot))
            {
                warnings.Add($"Skipped unsafe legacy path: {relative}");
                skipped++;
                continue;
            }

            CopyFile(file, target, ref copied, ref skipped, warnings);
        }
    }

    private static void CopyFile(
        string source,
        string destination,
        ref int copied,
        ref int skipped,
        ICollection<string> warnings)
    {
        try
        {
            if (File.Exists(destination))
            {
                skipped++;
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
            copied++;
        }
        catch (IOException exception)
        {
            skipped++;
            warnings.Add($"Could not migrate {Path.GetFileName(source)}: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            skipped++;
            warnings.Add($"Could not migrate {Path.GetFileName(source)}: {exception.Message}");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsWithin(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
