using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moonrise.Infrastructure;
using Moonrise.Models;

namespace Moonrise.Services;

public enum PackageImportStatus
{
    Imported,
    AlreadyImported,
    Invalid,
    Failed
}

public sealed record PackageImportResult(
    PackageImportStatus Status,
    PackageInfo? Package,
    string SourcePath,
    string Message);

public sealed record PackageImportSummary(
    int Imported,
    int AlreadyPresent,
    int Invalid,
    int Failed,
    IReadOnlyList<PackageImportResult> Results);

public sealed record PackageLaunchSelection(
    IReadOnlyList<PackageInfo> WeaveMods,
    IReadOnlyList<PackageInfo> JavaAgents);

public sealed record PackageStorageMigrationResult(
    int PackageFilesMigrated,
    int AddFilesMigrated,
    bool AlreadyComplete);

public sealed record PackageReconciliationResult(
    int Added,
    int Updated,
    int Removed,
    int RemovedEnabled,
    int IntegrityFailures,
    int DuplicatesRemoved,
    bool MetadataRebuilt,
    IReadOnlyList<string> Errors);

internal sealed class PackageIndexDocument
{
    public int FormatVersion { get; set; } = 1;
    public int MigrationVersion { get; set; }
    public int LayoutMigrationVersion { get; set; }
    public List<PackageIndexRecord> Packages { get; set; } = [];
}

internal sealed class PackageIndexRecord
{
    public string PackageId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ManagedFilePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public PackageKind Kind { get; set; }
    public string Version { get; set; } = "—";
    public string Entrypoint { get; set; } = "—";
    public string Identifier { get; set; } = string.Empty;
    public string SourceType { get; set; } = "manual";
    public string? SourceUrl { get; set; }
    public bool Enabled { get; set; }
    public string CompatibilityStatus { get; set; } = "untested";
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string LastIntegrityCheckResult { get; set; } = "not-checked";
    public List<string> CompatibleGameVersions { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];
    public List<string> Requires { get; set; } = [];
}

public sealed class LocalPackageLibrary
{
    public const long MaximumJarSize = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly JarMetadataParser _parser;
    private readonly object _sync = new();
    private PackageIndexDocument _index = new();
    private bool _metadataRebuilt;
    private sealed record PhysicalPackage(
        string Path,
        string Hash,
        PackageKind Kind,
        PackageInfo? Metadata,
        bool LegacyInvalid);

    public LocalPackageLibrary(AppPaths paths, JarMetadataParser parser)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public ObservableCollection<PackageInfo> Packages { get; } = [];
    public string IndexPath => _paths.PackageIndexPath;
    public IReadOnlyList<string> CategoryDirectories =>
        [_paths.WeavePackagesDirectory, _paths.AgentPackagesDirectory, _paths.UnclassifiedPackagesDirectory];
    public PackageStorageMigrationResult? LastLayoutMigrationResult { get; private set; }
    public PackageReconciliationResult? LastReconciliationResult { get; private set; }

    public void Load()
    {
        lock (_sync)
        {
            _paths.EnsureUserDirectories();
            _index = ReadIndex();
            LastLayoutMigrationResult = MigratePackageLayoutOnceCore();
            LastReconciliationResult = ReconcileCore(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ReplaceCollection();
        }
    }

    public PackageReconciliationResult Reconcile()
    {
        lock (_sync)
        {
            _paths.EnsureUserDirectories();
            LastReconciliationResult = ReconcileCore(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ReplaceCollection();
            return LastReconciliationResult;
        }
    }

    public PackageStorageMigrationResult MigratePackageLayoutOnce()
    {
        lock (_sync)
        {
            _paths.EnsureUserDirectories();
            var result = MigratePackageLayoutOnceCore();
            ReplaceCollection();
            return result;
        }
    }

    public int MigrateLegacyPackagesOnce(
        IEnumerable<string> disabledModNames,
        IEnumerable<string> disabledAgentNames)
    {
        lock (_sync)
        {
            if (_index.MigrationVersion >= 1)
                return 0;

            var disabledMods = disabledModNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var disabledAgents = disabledAgentNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var locations = new[]
            {
                (_paths.MoonriseOwnedModsDirectory, PackageKind.WeaveMod, true),
                (_paths.MoonriseOwnedAgentsDirectory, PackageKind.JavaAgent, true),
                (_paths.UserModsDirectory, PackageKind.WeaveMod, false),
                (_paths.UserAgentsDirectory, PackageKind.JavaAgent, false)
            };
            var added = 0;
            foreach (var (directory, expectedKind, preserveEnabled) in locations)
            {
                if (!Directory.Exists(directory))
                    continue;
                foreach (var path in Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var result = ImportCore(
                            path,
                            sourceType: "manual",
                            sourceUrl: null,
                            deleteIncomingAfterSuccess: false,
                            expectedKind: expectedKind,
                            enabledOverride: preserveEnabled &&
                                !(expectedKind == PackageKind.WeaveMod ? disabledMods : disabledAgents)
                                    .Contains(Path.GetFileName(path)));
                        if (result.Status == PackageImportStatus.Imported)
                            added++;
                    }
                    catch
                    {
                        // A broken legacy file is isolated; the rest of the Library still migrates.
                    }
                }
            }
            _index.MigrationVersion = 1;
            SaveIndex();
            ReplaceCollection();
            return added;
        }
    }

    public PackageImportResult Import(
        string sourcePath,
        string sourceType = "manual",
        string? sourceUrl = null)
    {
        lock (_sync)
        {
            var result = ImportCore(
                sourcePath,
                sourceType,
                sourceUrl,
                deleteIncomingAfterSuccess: false,
                expectedKind: null,
                enabledOverride: false);
            ReplaceCollection();
            return result;
        }
    }

    public PackageImportSummary ImportMany(IEnumerable<string> sourcePaths)
    {
        var results = new List<PackageImportResult>();
        foreach (var path in sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                results.Add(Import(path));
            }
            catch (InvalidDataException exception)
            {
                results.Add(new PackageImportResult(PackageImportStatus.Invalid, null, path, exception.Message));
            }
            catch (Exception exception)
            {
                results.Add(new PackageImportResult(PackageImportStatus.Failed, null, path, exception.Message));
            }
        }
        return Summarize(results);
    }

    public async Task<PackageImportSummary> ScanCategoryFoldersAsync(
        TimeSpan stableWindow,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureUserDirectories();
        var before = CategoryDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
            .ToDictionary(
                Path.GetFullPath,
                path =>
                {
                    var file = new FileInfo(path);
                    return (file.Length, file.LastWriteTimeUtc);
                },
                StringComparer.OrdinalIgnoreCase);
        if (stableWindow > TimeSpan.Zero)
            await Task.Delay(stableWindow, cancellationToken);

        var unstable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, signature) in before)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                continue;
            var file = new FileInfo(path);
            if (file.Length != signature.Length ||
                file.LastWriteTimeUtc != signature.LastWriteTimeUtc)
            {
                unstable.Add(path);
                continue;
            }
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                _ = stream.Length;
            }
            catch (IOException)
            {
                unstable.Add(path);
            }
        }

        PackageReconciliationResult reconciliation;
        string[] packageIdsBefore;
        lock (_sync)
        {
            packageIdsBefore = _index.Packages.Select(item => item.PackageId).ToArray();
            reconciliation = ReconcileCore(unstable);
            LastReconciliationResult = reconciliation;
            ReplaceCollection();
        }
        var knownBefore = packageIdsBefore.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = Packages.Select(package => new PackageImportResult(
                knownBefore.Contains(package.PackageId)
                    ? PackageImportStatus.AlreadyImported
                    : PackageImportStatus.Imported,
                package,
                package.FullPath,
                knownBefore.Contains(package.PackageId)
                    ? "Package is already imported."
                    : "Package imported."))
            .ToList();
        results.AddRange(unstable.Select(path => new PackageImportResult(
            PackageImportStatus.Failed,
            null,
            path,
            "File copy is still in progress.")));
        results.AddRange(reconciliation.Errors.Select(message => new PackageImportResult(
            PackageImportStatus.Invalid,
            null,
            string.Empty,
            message)));
        return Summarize(results);
    }

    public Task<PackageImportSummary> ScanIncomingAsync(
        TimeSpan stableWindow,
        CancellationToken cancellationToken = default) =>
        ScanCategoryFoldersAsync(stableWindow, cancellationToken);

    public void SetEnabled(PackageInfo package, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (_sync)
        {
            var record = FindRecord(package.PackageId);
            if (enabled && record.Kind is PackageKind.Ambiguous or PackageKind.Unclassified)
                throw new InvalidOperationException("Unclassified package cannot be launched.");
            record.Enabled = enabled;
            package.IsEnabled = enabled;
            SaveIndex();
        }
    }

    public void SelectType(PackageInfo package, PackageKind kind, bool developerMode)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (kind is not (PackageKind.WeaveMod or PackageKind.JavaAgent))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (package.Kind == PackageKind.Unclassified && !developerMode)
            throw new InvalidOperationException("Developer mode is required to classify this package manually.");

        lock (_sync)
        {
            var record = FindRecord(package.PackageId);
            PackageInfo? parsed = null;
            if (record.Kind != PackageKind.Unclassified)
            {
                parsed = kind == PackageKind.WeaveMod
                    ? _parser.ParseWeaveMod(package.FullPath)
                    : _parser.ParseJavaAgent(package.FullPath);
            }
            record.Kind = kind;
            if (parsed is not null)
            {
                record.DisplayName = parsed.DisplayName;
                record.Identifier = parsed.Identifier;
                record.Version = parsed.Version;
                record.Entrypoint = parsed.Entrypoint;
            }
            else
            {
                record.Entrypoint = "Manual classification";
            }
            record.Enabled = false;
            MoveRecordToCategory(record);
            SaveIndex();
            ReplaceCollection();
        }
    }

    public bool VerifyIntegrity(PackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (_sync)
        {
            var record = FindRecord(package.PackageId);
            var valid = File.Exists(package.FullPath) &&
                string.Equals(ComputeSha256(package.FullPath), record.Sha256, StringComparison.OrdinalIgnoreCase);
            record.LastIntegrityCheckResult = valid ? "verified" : "failed";
            if (!valid)
                record.Enabled = false;
            SaveIndex();
            ReplaceCollection();
            return valid;
        }
    }

    public void Remove(PackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (_sync)
        {
            var record = FindRecord(package.PackageId);
            _index.Packages.Remove(record);
            var referenced = _index.Packages.Any(item =>
                string.Equals(item.ManagedFilePath, record.ManagedFilePath, StringComparison.OrdinalIgnoreCase));
            if (!referenced)
            {
                var target = ResolveManagedPath(record.ManagedFilePath);
                EnsureInCategoryStorage(target);
                if (File.Exists(target))
                    File.Delete(target);
            }
            SaveIndex();
            ReplaceCollection();
        }
    }

    public long CalculateManagedStorageSize()
    {
        return CategoryDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
            .Select(path => new FileInfo(path).Length)
            .Sum();
    }

    private PackageReconciliationResult ReconcileCore(IReadOnlySet<string> unstablePaths)
    {
        var oldRecords = _index.Packages.ToArray();
        var physicalPackages = new List<PhysicalPackage>();
        var errors = new List<string>();
        var integrityFailures = 0;
        var duplicatesRemoved = 0;

        foreach (var originalPath in CategoryDirectories
                     .SelectMany(directory => Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
                     .Select(Path.GetFullPath)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (unstablePaths.Contains(originalPath))
                continue;

            string hash;
            try
            {
                hash = ComputeSha256(originalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(originalPath)}: {exception.Message}");
                continue;
            }

            var recordAtPath = oldRecords.FirstOrDefault(record =>
                TryResolveManagedPath(record.ManagedFilePath, out var indexedPath) &&
                string.Equals(indexedPath, originalPath, StringComparison.OrdinalIgnoreCase));
            if (recordAtPath is not null &&
                !string.Equals(recordAtPath.Sha256, hash, StringComparison.OrdinalIgnoreCase))
            {
                integrityFailures++;
            }

            PackageKind kind;
            PackageInfo? metadata;
            var legacyInvalid = recordAtPath is not null &&
                string.Equals(
                    recordAtPath.LastIntegrityCheckResult,
                    "legacy-invalid",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(recordAtPath.Sha256, hash, StringComparison.OrdinalIgnoreCase);
            try
            {
                ValidateSource(originalPath);
                (kind, metadata) = Detect(originalPath);
                legacyInvalid = false;
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                if (!legacyInvalid)
                {
                    errors.Add($"{Path.GetFileName(originalPath)}: {exception.Message}");
                    continue;
                }
                kind = PackageKind.Unclassified;
                metadata = null;
            }

            var path = originalPath;
            var correctDirectory = GetCategoryDirectory(kind);
            if (!IsInside(path, correctDirectory))
            {
                var destination = SelectFriendlyDestination(
                    correctDirectory,
                    Path.GetFileName(path),
                    hash);
                StoreManagedCopy(path, destination, hash);
                PreserveLastWriteTime(path, destination);
                VerifyPathHash(path, hash);
                VerifyPathHash(destination, hash);
                if (!string.Equals(path, destination, StringComparison.OrdinalIgnoreCase))
                    File.Delete(path);
                path = destination;
            }

            physicalPackages.Add(new PhysicalPackage(path, hash, kind, metadata, legacyInvalid));
        }

        var canonicalPackages = new List<PhysicalPackage>();
        foreach (var hashGroup in physicalPackages.GroupBy(
                     item => item.Hash,
                     StringComparer.OrdinalIgnoreCase))
        {
            var indexedPath = oldRecords
                .Where(record => string.Equals(record.Sha256, hashGroup.Key, StringComparison.OrdinalIgnoreCase))
                .Select(record => TryResolveManagedPath(record.ManagedFilePath, out var path) ? path : null)
                .FirstOrDefault(path => path is not null && hashGroup.Any(item =>
                    string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)));
            var canonical = hashGroup
                .OrderByDescending(item => indexedPath is not null &&
                    string.Equals(item.Path, indexedPath, StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .First();
            canonicalPackages.Add(canonical);
            foreach (var duplicate in hashGroup.Where(item =>
                         !string.Equals(item.Path, canonical.Path, StringComparison.OrdinalIgnoreCase)))
            {
                VerifyPathHash(duplicate.Path, hashGroup.Key);
                File.Delete(duplicate.Path);
                duplicatesRemoved++;
            }
        }

        var reconciled = new List<PackageIndexRecord>();
        var added = 0;
        var updated = 0;
        foreach (var physical in canonicalPackages)
        {
            var record = oldRecords.FirstOrDefault(item =>
                string.Equals(item.Sha256, physical.Hash, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                record = CreateRecord(
                    physical.Path,
                    physical.Path,
                    physical.Hash,
                    physical.Kind,
                    physical.Metadata,
                    sourceType: "manual",
                    sourceUrl: null,
                    enabled: false);
                if (physical.LegacyInvalid)
                    record.LastIntegrityCheckResult = "legacy-invalid";
                added++;
            }
            else
            {
                var before = JsonSerializer.Serialize(record, JsonOptions);
                UpdateRecordFromPhysical(record, physical);
                if (!string.Equals(before, JsonSerializer.Serialize(record, JsonOptions), StringComparison.Ordinal))
                    updated++;
            }
            reconciled.Add(record);
        }

        foreach (var unstablePath in unstablePaths.Where(File.Exists))
        {
            var existing = oldRecords.FirstOrDefault(record =>
                TryResolveManagedPath(record.ManagedFilePath, out var path) &&
                string.Equals(path, unstablePath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null &&
                reconciled.All(record => !string.Equals(
                    record.PackageId,
                    existing.PackageId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                existing.Enabled = false;
                existing.LastIntegrityCheckResult = "copy-in-progress";
                reconciled.Add(existing);
            }
        }

        var retainedIds = reconciled
            .Select(item => item.PackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedRecords = oldRecords
            .Where(item => !retainedIds.Contains(item.PackageId))
            .ToArray();
        var removedEnabled = removedRecords.Count(item => item.Enabled);

        _index.Packages = reconciled;
        _index.LayoutMigrationVersion = Math.Max(_index.LayoutMigrationVersion, 4);
        SaveIndex();
        RemoveEmptyLegacyDirectories();
        var metadataRebuilt = _metadataRebuilt;
        _metadataRebuilt = false;
        return new PackageReconciliationResult(
            added,
            updated,
            removedRecords.Length,
            removedEnabled,
            integrityFailures,
            duplicatesRemoved,
            metadataRebuilt,
            errors);
    }

    private void UpdateRecordFromPhysical(PackageIndexRecord record, PhysicalPackage physical)
    {
        record.ManagedFilePath = Path.GetRelativePath(_paths.RootDirectory, physical.Path);
        record.OriginalFileName = Path.GetFileName(physical.Path);
        record.FileSize = new FileInfo(physical.Path).Length;
        record.Kind = physical.Kind;
        record.Enabled &= physical.Kind is PackageKind.WeaveMod or PackageKind.JavaAgent;
        record.LastIntegrityCheckResult = physical.LegacyInvalid ? "legacy-invalid" : "verified";
        if (physical.Metadata is not null)
        {
            record.DisplayName = physical.Metadata.DisplayName;
            record.Version = physical.Metadata.Version;
            record.Entrypoint = physical.Metadata.Entrypoint;
            record.Identifier = physical.Metadata.Identifier;
            record.CompatibleGameVersions = physical.Metadata.CompatibleGameVersions.ToList();
            record.Conflicts = physical.Metadata.Conflicts.ToList();
            record.Requires = physical.Metadata.Requires.ToList();
        }
        else if (string.IsNullOrWhiteSpace(record.DisplayName))
        {
            record.DisplayName = Path.GetFileNameWithoutExtension(physical.Path);
        }
    }

    private PackageStorageMigrationResult MigratePackageLayoutOnceCore()
    {
        const int targetVersion = 4;
        if (_index.LayoutMigrationVersion >= targetVersion &&
            File.Exists(_paths.PackageLayoutMigrationMarkerPath) &&
            !HasPendingLegacyMigration())
        {
            return new PackageStorageMigrationResult(0, 0, AlreadyComplete: true);
        }

        var migratedPackages = 0;
        var migratedLegacyFiles = 0;
        var migrationComplete = true;
        var migrationLog = new List<string>
        {
            $"Moonrise package layout migration v{targetVersion}",
            $"StartedUtc={DateTimeOffset.UtcNow:O}"
        };
        foreach (var record in _index.Packages)
        {
            var current = ResolveMigrationSource(record.ManagedFilePath, record.Sha256);
            if (current is null)
            {
                record.Enabled = false;
                record.LastIntegrityCheckResult = "missing-during-migration";
                migrationComplete = false;
                migrationLog.Add($"MISSING indexed package {record.PackageId} ({record.OriginalFileName})");
                continue;
            }

            var destinationDirectory = GetCategoryDirectory(record.Kind);
            if (IsInside(current, destinationDirectory))
            {
                VerifyPathHash(current, record.Sha256);
                continue;
            }

            var destination = SelectFriendlyDestination(
                destinationDirectory,
                record.OriginalFileName,
                record.Sha256);
            StoreManagedCopy(current, destination, record.Sha256);
            PreserveLastWriteTime(current, destination);
            record.ManagedFilePath = Path.GetRelativePath(_paths.RootDirectory, destination);
            SaveIndex();
            VerifyManagedCopy(record);
            if (!string.Equals(current, destination, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(current) &&
                IsLegacyManagedPath(current))
            {
                File.Delete(current);
            }
            migrationLog.Add(
                $"MIGRATED indexed {record.PackageId} -> {Path.GetRelativePath(_paths.PackagesDirectory, destination)}");
            migratedPackages++;
        }

        foreach (var source in EnumerateLegacyJarFiles().ToArray())
        {
            if (!File.Exists(source))
                continue;
            string hash;
            try
            {
                hash = ComputeSha256(source);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                migrationComplete = false;
                migrationLog.Add($"UNREADABLE preserved {source}: {exception.GetType().Name}");
                continue;
            }

            var existing = _index.Packages.FirstOrDefault(item =>
                string.Equals(item.Sha256, hash, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                VerifyManagedCopy(existing);
                VerifyPathHash(source, hash);
                if (!string.Equals(source, ResolveManagedPath(existing.ManagedFilePath), StringComparison.OrdinalIgnoreCase))
                    File.Delete(source);
                migrationLog.Add($"REMOVED verified redundant legacy copy {source}; SHA-256={hash}");
                migratedLegacyFiles++;
                continue;
            }

            PackageKind kind;
            PackageInfo? metadata;
            var invalid = false;
            try
            {
                ValidateSource(source);
                (kind, metadata) = Detect(source);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                kind = PackageKind.Unclassified;
                metadata = null;
                invalid = true;
                migrationLog.Add($"INVALID legacy JAR preserved; SHA-256={hash}; source={source}; reason={exception.Message}");
            }

            var destination = invalid
                ? SelectInvalidLegacyDestination(source, hash)
                : SelectFriendlyDestination(
                    GetCategoryDirectory(kind),
                    Path.GetFileName(source),
                    hash);
            StoreVerifiedFile(source, destination, hash, ".migrating");
            PreserveLastWriteTime(source, destination);
            VerifyPathHash(destination, hash);

            var record = CreateRecord(
                source,
                destination,
                hash,
                kind,
                metadata,
                sourceType: "legacy",
                sourceUrl: null,
                enabled: false);
            record.LastIntegrityCheckResult = invalid ? "legacy-invalid" : "verified";
            _index.Packages.Add(record);
            SaveIndex();
            VerifyManagedCopy(record);
            if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                File.Delete(source);
            migrationLog.Add(
                $"{(invalid ? "PRESERVED invalid" : "MIGRATED unique")} legacy JAR -> " +
                $"{Path.GetRelativePath(_paths.PackagesDirectory, destination)}; SHA-256={hash}");
            migratedLegacyFiles++;
        }

        migrationComplete &= PreserveLegacyNonJarFiles(migrationLog);
        WriteMigrationLog(migrationLog);
        if (!migrationComplete)
        {
            SaveIndex();
            return new PackageStorageMigrationResult(
                migratedPackages,
                migratedLegacyFiles,
                AlreadyComplete: false);
        }

        RemoveEmptyLegacyDirectories();
        _index.LayoutMigrationVersion = targetVersion;
        SaveIndex();
        File.WriteAllText(
            _paths.PackageLayoutMigrationMarkerPath,
            $"layoutVersion={targetVersion}{Environment.NewLine}completedUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}",
            new UTF8Encoding(false));
        return new PackageStorageMigrationResult(migratedPackages, migratedLegacyFiles, AlreadyComplete: false);
    }

    private bool HasPendingLegacyMigration()
    {
        if (LegacyPackageDirectories().Any(root =>
                Directory.Exists(root) &&
                Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Any()))
        {
            return true;
        }

        return _index.Packages.Any(record =>
            !TryResolveManagedPath(record.ManagedFilePath, out var path) ||
            !File.Exists(path));
    }

    private bool PreserveLegacyNonJarFiles(ICollection<string> migrationLog)
    {
        var complete = true;
        foreach (var root in LegacyPackageDirectories().Where(Directory.Exists))
        {
            string[] files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => !string.Equals(
                        Path.GetExtension(path),
                        ".jar",
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                migrationLog.Add(
                    $"UNREADABLE legacy directory preserved {root}: {exception.GetType().Name}");
                complete = false;
                continue;
            }

            foreach (var source in files)
            {
                try
                {
                    var legacyRootName = Path.GetFileName(root);
                    var relative = Path.GetRelativePath(root, source);
                    var destination = SelectPreservedLegacyMetadataDestination(
                        Path.Combine(_paths.LegacyPreservedMetadataDirectory, legacyRootName, relative));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(source, destination, overwrite: false);
                    migrationLog.Add(
                        $"PRESERVED legacy metadata {source} -> " +
                        $"{Path.GetRelativePath(_paths.PackagesDirectory, destination)}");
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    migrationLog.Add(
                        $"UNREADABLE legacy file preserved {source}: {exception.GetType().Name}");
                    complete = false;
                }
            }
        }
        return complete;
    }

    private static string SelectPreservedLegacyMetadataDestination(string requested)
    {
        if (!File.Exists(requested))
            return requested;
        var directory = Path.GetDirectoryName(requested)!;
        var stem = Path.GetFileNameWithoutExtension(requested);
        var extension = Path.GetExtension(requested);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{index}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException("Unable to preserve a legacy metadata file without overwriting it.");
    }

    private IEnumerable<string> EnumerateLegacyJarFiles()
    {
        foreach (var root in LegacyPackageDirectories())
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var path in Directory.EnumerateFiles(root, "*.jar", SearchOption.AllDirectories))
                yield return path;
        }
    }

    private string? ResolveMigrationSource(string storedPath, string expectedHash)
    {
        string current;
        try
        {
            current = Path.IsPathFullyQualified(storedPath)
                ? Path.GetFullPath(storedPath)
                : Path.GetFullPath(Path.Combine(_paths.RootDirectory, storedPath));
        }
        catch
        {
            current = string.Empty;
        }
        if (!string.IsNullOrEmpty(current) && File.Exists(current))
        {
            VerifyPathHash(current, expectedHash);
            return current;
        }

        return Directory.Exists(_paths.PackagesDirectory)
            ? Directory.EnumerateFiles(_paths.PackagesDirectory, "*.jar", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                {
                    try
                    {
                        return string.Equals(
                            ComputeSha256(path),
                            expectedHash,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                })
            : null;
    }

    private void MoveRecordToCategory(PackageIndexRecord record)
    {
        var source = ResolveManagedPath(record.ManagedFilePath);
        var destinationDirectory = GetCategoryDirectory(record.Kind);
        if (IsInside(source, destinationDirectory))
            return;

        VerifyPathHash(source, record.Sha256);
        var destination = SelectFriendlyDestination(
            destinationDirectory,
            record.OriginalFileName,
            record.Sha256);
        StoreManagedCopy(source, destination, record.Sha256);
        PreserveLastWriteTime(source, destination);
        record.ManagedFilePath = Path.GetRelativePath(_paths.RootDirectory, destination);
        SaveIndex();
        VerifyManagedCopy(record);
        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            File.Delete(source);
    }

    private string GetCategoryDirectory(PackageKind kind) => kind switch
    {
        PackageKind.WeaveMod => _paths.WeavePackagesDirectory,
        PackageKind.JavaAgent => _paths.AgentPackagesDirectory,
        _ => _paths.UnclassifiedPackagesDirectory
    };

    private IEnumerable<string> LegacyPackageDirectories()
    {
        yield return _paths.LegacyAddPackagesDirectory;
        yield return _paths.LegacyImportedPackagesDirectory;
        yield return _paths.LegacyIncomingPackagesDirectory;
        yield return _paths.LegacyInstalledPackagesDirectory;
        yield return _paths.LegacyMoonriseOwnedPackagesDirectory;
        yield return _paths.LegacyOriginalsPackagesDirectory;
    }

    private bool IsLegacyManagedPath(string path) =>
        LegacyPackageDirectories().Any(root => IsInside(path, root));

    private bool IsInCategoryStorage(string path) =>
        CategoryDirectories.Any(root => IsInside(path, root));

    private string SelectInvalidLegacyDestination(string source, string hash)
    {
        var friendly = SanitizeFriendlyJarFileName(Path.GetFileName(source));
        var stem = Path.GetFileNameWithoutExtension(friendly);
        return SelectFriendlyDestination(
            _paths.UnclassifiedPackagesDirectory,
            $"{stem}--legacy-invalid-{hash[..8].ToLowerInvariant()}.jar",
            hash);
    }

    private void RemoveEmptyLegacyDirectories()
    {
        foreach (var root in LegacyPackageDirectories().Where(Directory.Exists))
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any())
                Directory.Delete(root);
        }
    }

    private void WriteMigrationLog(IEnumerable<string> lines)
    {
        Directory.CreateDirectory(_paths.PackageMetadataDirectory);
        var temporary = _paths.PackageLayoutMigrationLogPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
            File.Move(temporary, _paths.PackageLayoutMigrationLogPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string SelectFriendlyDestination(
        string directory,
        string originalFileName,
        string hash)
    {
        Directory.CreateDirectory(directory);
        var friendlyName = SanitizeFriendlyJarFileName(originalFileName);
        var direct = Path.Combine(directory, friendlyName);
        if (!File.Exists(direct) ||
            string.Equals(ComputeSha256(direct), hash, StringComparison.OrdinalIgnoreCase))
        {
            return direct;
        }

        var stem = Path.GetFileNameWithoutExtension(friendlyName);
        var suffix = hash.ToLowerInvariant()[..Math.Min(8, hash.Length)];
        for (var collision = 0; collision < 1000; collision++)
        {
            var discriminator = collision == 0 ? string.Empty : $"-{collision + 1}";
            var candidate = Path.Combine(directory, $"{stem}--{suffix}{discriminator}.jar");
            if (!File.Exists(candidate) ||
                string.Equals(ComputeSha256(candidate), hash, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        throw new IOException("Unable to select a collision-safe installed package filename.");
    }

    internal static string SanitizeFriendlyJarFileName(string originalFileName)
    {
        var fileName = Path.GetFileName(originalFileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        stem = new string(stem.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(stem))
            stem = "package";
        if (stem.Length > 120)
            stem = stem[..120].TrimEnd();
        return stem + ".jar";
    }

    private static void StoreVerifiedFile(
        string source,
        string destination,
        string hash,
        string temporarySuffix)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            VerifyPathHash(destination, hash);
            return;
        }

        var temporary = destination + $".{Guid.NewGuid():N}{temporarySuffix}";
        try
        {
            File.Copy(source, temporary, overwrite: false);
            VerifyPathHash(source, hash);
            VerifyPathHash(temporary, hash);
            File.Move(temporary, destination, overwrite: false);
            VerifyPathHash(destination, hash);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void PreserveLastWriteTime(string source, string destination)
    {
        try { File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void VerifyPathHash(string path, string expectedHash)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The package file is missing.", path);
        if (!string.Equals(ComputeSha256(path), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package integrity verification failed.");
    }

    private PackageImportResult ImportCore(
        string sourcePath,
        string sourceType,
        string? sourceUrl,
        bool deleteIncomingAfterSuccess,
        PackageKind? expectedKind,
        bool enabledOverride)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("JAR file was not found.", source);
        if (!string.Equals(Path.GetExtension(source), ".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .jar files are supported.");
        var recordAtSource = _index.Packages.FirstOrDefault(item =>
        {
            try
            {
                return string.Equals(
                    ResolveManagedPath(item.ManagedFilePath),
                    source,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        });
        var sourceInfo = new FileInfo(source);
        if (recordAtSource is null &&
            (sourceInfo.Length <= 0 || sourceInfo.Length > MaximumJarSize))
        {
            throw new InvalidDataException("JAR size must be between 1 byte and 512 MB.");
        }
        var hash = ComputeSha256(source);
        if (recordAtSource is not null &&
            !string.Equals(recordAtSource.Sha256, hash, StringComparison.OrdinalIgnoreCase))
        {
            recordAtSource.Enabled = false;
            recordAtSource.LastIntegrityCheckResult = "failed";
            SaveIndex();
            throw new InvalidDataException("An installed JAR was changed and failed integrity verification.");
        }
        if (recordAtSource is not null &&
            string.Equals(
                recordAtSource.LastIntegrityCheckResult,
                "legacy-invalid",
                StringComparison.OrdinalIgnoreCase))
        {
            return new PackageImportResult(
                PackageImportStatus.AlreadyImported,
                ToPackage(recordAtSource),
                source,
                "Invalid legacy JAR remains preserved and disabled.");
        }

        ValidateSource(source);

        var duplicate = _index.Packages.FirstOrDefault(item =>
            string.Equals(item.Sha256, hash, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            VerifyManagedCopy(duplicate);
            var managedPath = ResolveManagedPath(duplicate.ManagedFilePath);
            if ((deleteIncomingAfterSuccess || IsInCategoryStorage(source)) &&
                !string.Equals(source, managedPath, StringComparison.OrdinalIgnoreCase))
            {
                VerifyPathHash(source, hash);
                File.Delete(source);
            }
            return new PackageImportResult(
                PackageImportStatus.AlreadyImported,
                ToPackage(duplicate),
                source,
                "Package is already imported.");
        }

        var detection = Detect(source);
        if (expectedKind is { } expected &&
            detection.Kind is PackageKind.WeaveMod or PackageKind.JavaAgent &&
            detection.Kind != expected)
        {
            throw new InvalidDataException("The stored package type does not match its legacy location.");
        }

        var destinationDirectory = GetCategoryDirectory(detection.Kind);
        var sourceAlreadyCorrect = IsInside(source, destinationDirectory);
        var destination = sourceAlreadyCorrect
            ? source
            : SelectFriendlyDestination(destinationDirectory, Path.GetFileName(source), hash);
        if (!sourceAlreadyCorrect)
        {
            StoreManagedCopy(source, destination, hash);
            PreserveLastWriteTime(source, destination);
        }

        var record = CreateRecord(
            source,
            destination,
            hash,
            detection.Kind,
            detection.Metadata,
            sourceType,
            sourceUrl,
            enabledOverride && detection.Kind is PackageKind.WeaveMod or PackageKind.JavaAgent);
        _index.Packages.Add(record);
        SaveIndex();
        VerifyManagedCopy(record);
        if (!sourceAlreadyCorrect &&
            (deleteIncomingAfterSuccess || IsInCategoryStorage(source)))
        {
            VerifyPathHash(source, hash);
            File.Delete(source);
        }
        return new PackageImportResult(
            PackageImportStatus.Imported,
            ToPackage(record),
            source,
            !sourceAlreadyCorrect && IsInCategoryStorage(source)
                ? "Package moved to its detected category and imported."
                : "Package imported.");
    }

    private PackageIndexRecord CreateRecord(
        string source,
        string destination,
        string hash,
        PackageKind kind,
        PackageInfo? metadata,
        string sourceType,
        string? sourceUrl,
        bool enabled) =>
        new()
        {
            PackageId = "sha256:" + hash.ToLowerInvariant(),
            DisplayName = metadata?.DisplayName ?? Path.GetFileNameWithoutExtension(source),
            OriginalFileName = Path.GetFileName(source),
            ManagedFilePath = Path.GetRelativePath(_paths.RootDirectory, destination),
            Sha256 = hash,
            FileSize = new FileInfo(destination).Length,
            Kind = kind,
            Version = metadata?.Version ?? "—",
            Entrypoint = metadata?.Entrypoint ?? "—",
            Identifier = metadata?.Identifier ?? "sha256:" + hash.ToLowerInvariant(),
            SourceType = sourceType,
            SourceUrl = sourceUrl,
            Enabled = enabled,
            CompatibilityStatus = "untested",
            ImportedAtUtc = DateTimeOffset.UtcNow,
            LastIntegrityCheckResult = "verified",
            CompatibleGameVersions = metadata?.CompatibleGameVersions.ToList() ?? [],
            Conflicts = metadata?.Conflicts.ToList() ?? [],
            Requires = metadata?.Requires.ToList() ?? []
        };

    private (PackageKind Kind, PackageInfo? Metadata) Detect(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var weaveEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, "weave.mod.json", StringComparison.OrdinalIgnoreCase));
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName.Replace('\\', '/'), "META-INF/MANIFEST.MF", StringComparison.OrdinalIgnoreCase));

        PackageInfo? weave = null;
        if (weaveEntry is not null)
        {
            try
            {
                weave = _parser.ParseWeaveMod(path);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                throw new InvalidDataException("weave.mod.json is malformed.", exception);
            }
        }

        var isAgent = false;
        if (manifestEntry is not null)
        {
            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var manifest = reader.ReadToEnd();
            isAgent = HasManifestValue(manifest, "Premain-Class") ||
                HasManifestValue(manifest, "Agent-Class");
        }
        PackageInfo? agent = isAgent ? _parser.ParseJavaAgent(path) : null;

        if (weave is not null && agent is not null)
            return (PackageKind.Ambiguous, weave);
        if (weave is not null)
            return (PackageKind.WeaveMod, weave);
        if (agent is not null)
            return (PackageKind.JavaAgent, agent);
        return (PackageKind.Unclassified, null);
    }

    private static bool HasManifestValue(string raw, string name)
    {
        var unfolded = raw.Replace("\r\n ", string.Empty, StringComparison.Ordinal)
            .Replace("\n ", string.Empty, StringComparison.Ordinal);
        return unfolded.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line =>
            {
                var separator = line.IndexOf(':');
                return separator > 0 &&
                    string.Equals(line[..separator].Trim(), name, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(line[(separator + 1)..]);
            });
    }

    private static async Task<bool> WaitUntilStableAsync(
        string path,
        TimeSpan stableWindow,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;
        var before = new FileInfo(path);
        var length = before.Length;
        var write = before.LastWriteTimeUtc;
        await Task.Delay(stableWindow, cancellationToken);
        if (!File.Exists(path))
            return false;
        var after = new FileInfo(path);
        if (after.Length != length || after.LastWriteTimeUtc != write)
            return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Length == after.Length;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static PackageImportSummary Summarize(IReadOnlyList<PackageImportResult> results) => new(
        results.Count(item => item.Status == PackageImportStatus.Imported),
        results.Count(item => item.Status == PackageImportStatus.AlreadyImported),
        results.Count(item => item.Status == PackageImportStatus.Invalid),
        results.Count(item => item.Status == PackageImportStatus.Failed),
        results);

    private void ValidateSource(string source)
    {
        if (!File.Exists(source))
            throw new FileNotFoundException("JAR file was not found.", source);
        if (!string.Equals(Path.GetExtension(source), ".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .jar files are supported.");
        var info = new FileInfo(source);
        if (info.Length <= 0 || info.Length > MaximumJarSize)
            throw new InvalidDataException("JAR size must be between 1 byte and 512 MB.");
        try
        {
            using var archive = ZipFile.OpenRead(source);
            if (archive.Entries.Count == 0)
                throw new InvalidDataException("JAR file is invalid.");
            foreach (var entry in archive.Entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                if (normalized.StartsWith('/') || normalized.Split('/').Any(part => part == ".."))
                    throw new InvalidDataException("JAR contains an unsafe archive path.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("JAR file is invalid.", exception);
        }
    }

    private void StoreManagedCopy(string source, string destination, string hash)
    {
        var destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("Managed package destination is invalid.");
        EnsureInCategoryStorage(destination);
        Directory.CreateDirectory(destinationDirectory);
        if (File.Exists(destination))
        {
            if (!string.Equals(ComputeSha256(destination), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A different JAR already uses the selected installed filename.");
            return;
        }

        var temporary = Path.Combine(
            destinationDirectory,
            $".{hash.ToLowerInvariant()}.{Guid.NewGuid():N}.importing");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            if (!string.Equals(ComputeSha256(temporary), hash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ComputeSha256(source), hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Managed copy hash does not match the source JAR.");
            }
            File.Move(temporary, destination, overwrite: false);
            if (!string.Equals(ComputeSha256(destination), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Managed copy verification failed.");
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private PackageIndexDocument ReadIndex()
    {
        RecoverInterruptedIndexWrite();
        if (!File.Exists(_paths.PackageIndexPath))
        {
            _metadataRebuilt = true;
            return new PackageIndexDocument();
        }
        try
        {
            var document = JsonSerializer.Deserialize<PackageIndexDocument>(
                               File.ReadAllText(_paths.PackageIndexPath),
                               JsonOptions)
                           ?? throw new JsonException("The package index is empty.");
            if (document.Packages is null ||
                document.Packages.Any(record =>
                    record is null ||
                    string.IsNullOrWhiteSpace(record.ManagedFilePath) ||
                    !IsSha256(record.Sha256)))
            {
                throw new JsonException("The package index contains invalid records.");
            }
            foreach (var record in document.Packages)
            {
                record.CompatibleGameVersions ??= [];
                record.Conflicts ??= [];
                record.Requires ??= [];
                if (string.IsNullOrWhiteSpace(record.PackageId))
                    record.PackageId = "sha256:" + record.Sha256.ToLowerInvariant();
            }
            return document;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            PreserveCorruptIndex();
            _metadataRebuilt = true;
            return new PackageIndexDocument();
        }
    }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
            return false;
        try
        {
            return Convert.FromHexString(value).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void RecoverInterruptedIndexWrite()
    {
        if (File.Exists(_paths.PackageIndexPath) ||
            !Directory.Exists(_paths.PackageMetadataDirectory))
        {
            return;
        }

        foreach (var candidate in Directory.EnumerateFiles(
                     _paths.PackageMetadataDirectory,
                     "index.json.*.tmp",
                     SearchOption.TopDirectoryOnly)
                 .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var document = JsonSerializer.Deserialize<PackageIndexDocument>(
                    File.ReadAllText(candidate),
                    JsonOptions);
                if (document is null)
                    continue;
                File.Move(candidate, _paths.PackageIndexPath, overwrite: false);
                return;
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void PreserveCorruptIndex()
    {
        Directory.CreateDirectory(_paths.PackageMetadataRecoveryDirectory);
        var destination = Path.Combine(
            _paths.PackageMetadataRecoveryDirectory,
            $"index-corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        File.Copy(_paths.PackageIndexPath, destination, overwrite: false);
    }

    private void SaveIndex()
    {
        Directory.CreateDirectory(_paths.PackageMetadataDirectory);
        var temporary = _paths.PackageIndexPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, _index, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _paths.PackageIndexPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private void ReplaceCollection()
    {
        Packages.Clear();
        foreach (var package in _index.Packages
                     .Select(ToPackage)
                     .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Packages.Add(package);
        }
    }

    private PackageInfo ToPackage(PackageIndexRecord record) => new()
    {
        PackageId = record.PackageId,
        FileName = record.OriginalFileName,
        OriginalFileName = record.OriginalFileName,
        DisplayName = record.DisplayName,
        Identifier = record.Identifier,
        FullPath = ResolveManagedPath(record.ManagedFilePath),
        Kind = record.Kind,
        Version = record.Version,
        Entrypoint = record.Entrypoint,
        Size = record.FileSize,
        Sha256 = record.Sha256,
        SourceType = record.SourceType,
        SourceUrl = record.SourceUrl,
        IsEnabled = record.Enabled,
        CompatibilityStatus = record.CompatibilityStatus,
        ImportedAtUtc = record.ImportedAtUtc,
        LastIntegrityCheckResult = record.LastIntegrityCheckResult,
        CompatibleGameVersions = record.CompatibleGameVersions,
        Conflicts = record.Conflicts,
        Requires = record.Requires
    };

    private PackageIndexRecord FindRecord(string packageId) =>
        _index.Packages.FirstOrDefault(item =>
            string.Equals(item.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException("Package is not present in the Library.");

    private void VerifyManagedCopy(PackageIndexRecord record)
    {
        var path = ResolveManagedPath(record.ManagedFilePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("The managed package file is missing.", path);
        if (!string.Equals(ComputeSha256(path), record.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package integrity check failed.");
    }

    private string ResolveManagedPath(string storedPath)
    {
        var path = Path.GetFullPath(Path.Combine(_paths.RootDirectory, storedPath));
        EnsureInCategoryStorage(path);
        return path;
    }

    private bool TryResolveManagedPath(string storedPath, out string path)
    {
        try
        {
            path = ResolveManagedPath(storedPath);
            return true;
        }
        catch
        {
            path = string.Empty;
            return false;
        }
    }

    private void EnsureInCategoryStorage(string path)
    {
        if (!IsInCategoryStorage(path))
            throw new InvalidDataException("Managed package path escapes the category package store.");
    }

    private static void EnsureInside(string path, string root)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Managed package path escapes the package store.");
    }

    private static bool IsInside(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

public sealed class PackageLaunchResolver
{
    private readonly JarMetadataParser _parser;
    private readonly AppPaths? _paths;

    public PackageLaunchResolver(JarMetadataParser parser, AppPaths? paths = null)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _paths = paths;
    }

    public PackageLaunchSelection Resolve(IEnumerable<PackageInfo> packages, bool excludeAll)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return excludeAll ? new PackageLaunchSelection([], []) : Resolve(packages);
    }

    public PackageLaunchSelection Resolve(IEnumerable<PackageInfo> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var enabled = packages.Where(item => item.IsEnabled)
            .GroupBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var unsupported = enabled.FirstOrDefault(item =>
            item.Kind is PackageKind.Ambiguous or PackageKind.Unclassified);
        if (unsupported is not null)
            throw new InvalidOperationException(
                $"{unsupported.OriginalFileName}: Unclassified package cannot be launched.");

        foreach (var package in enabled)
        {
            if (_paths is not null && !IsAuthoritativePath(package.FullPath))
                throw new InvalidDataException(
                    $"{package.OriginalFileName}: Package path is outside the authoritative category folders.");
            if (!File.Exists(package.FullPath))
                throw new FileNotFoundException("The managed package file is missing.", package.FullPath);
            var actual = LocalPackageLibrary.ComputeSha256(package.FullPath);
            if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{package.OriginalFileName}: Package integrity check failed.");
            if (!string.Equals(package.Entrypoint, "Manual classification", StringComparison.Ordinal))
            {
                _ = package.Kind switch
                {
                    PackageKind.WeaveMod => _parser.ParseWeaveMod(package.FullPath),
                    PackageKind.JavaAgent => _parser.ParseJavaAgent(package.FullPath),
                    _ => throw new InvalidOperationException("Unclassified package cannot be launched.")
                };
            }
        }

        return new PackageLaunchSelection(
            enabled.Where(item => item.Kind == PackageKind.WeaveMod).ToArray(),
            enabled.Where(item => item.Kind == PackageKind.JavaAgent).ToArray());
    }

    private bool IsAuthoritativePath(string path)
    {
        var full = Path.GetFullPath(path);
        return new[]
        {
            _paths!.WeavePackagesDirectory,
            _paths.AgentPackagesDirectory,
            _paths.UnclassifiedPackagesDirectory
        }.Any(root =>
        {
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        });
    }
}
