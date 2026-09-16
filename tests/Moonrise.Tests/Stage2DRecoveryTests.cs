using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class Stage2DRecoveryTests
{
    [Fact]
    public void DetachedElectronReplacementOutsideOriginalTreeIsTrackedAndHidden()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var identity = Identity(10, launcherPath, now);
        var processes = new MutableProcesses([
            new(20, 1, "Lunar Client", launcherPath, now.AddMilliseconds(100))
        ]);
        var windows = new TestWindows([
            new((nint)200, 20, true, "Chrome_WidgetWin_1", "Lunar Client")
        ]);
        var audit = new List<string>();
        var service = new LunarBackgroundLaunchService(
            identity,
            processes,
            windows,
            audit: audit.Add);

        var hidden = service.HideOwnedWindows();

        Assert.Equal(1, hidden);
        Assert.Contains(20, service.OwnedProcessIds);
        Assert.Contains((nint)200, windows.Hidden);
        Assert.Contains(audit, line =>
            line.Contains("Detached Lunar process tracked", StringComparison.Ordinal));
        Assert.Contains(audit, line =>
            line.Contains("classifiedLunar=True", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingAndNewlyShownLunarWindowsAreHiddenImmediately()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var processes = new MutableProcesses([
            new(10, 1, "Lunar Client", launcherPath, now)
        ]);
        var windows = new TestWindows([
            new((nint)100, 10, true, "Chrome_WidgetWin_1", "Lunar Client")
        ]);
        var events = new FakeWindowEvents();
        var service = new LunarBackgroundLaunchService(
            Identity(10, launcherPath, now),
            processes,
            windows,
            events);
        using var cancellation = new CancellationTokenSource();
        var watch = service.WatchAsync(cancellation.Token);

        windows.Add(new WindowEntry(
            (nint)101,
            10,
            true,
            "Chrome_WidgetWin_1",
            "Lunar Client"));
        events.Raise((nint)101);

        Assert.Equal([(nint)100, (nint)101], windows.Hidden);
        cancellation.Cancel();
        await watch;
    }

    [Fact]
    public void FailedHideIsVerifiedAndRetriedBoundedly()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var processes = new MutableProcesses([
            new(10, 1, "Lunar Client", launcherPath, now)
        ]);
        var windows = new TestWindows([
            new((nint)100, 10, true, "Chrome_WidgetWin_1", "Lunar Client")
        ])
        {
            FailuresBeforeHide = 5
        };
        var audit = new List<string>();
        var service = new LunarBackgroundLaunchService(
            Identity(10, launcherPath, now),
            processes,
            windows,
            audit: audit.Add);

        Assert.Equal(0, service.HideOwnedWindows());
        Assert.Equal(3, windows.Hidden.Count);
        Assert.True(windows.IsVisible((nint)100));
        Assert.Contains(audit, line =>
            line.Contains("hide=failed-visible;attempts=3", StringComparison.Ordinal));
    }

    [Fact]
    public void UnrelatedElectronAndMinecraftWindowsAreExcluded()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var processes = new MutableProcesses([
            new(10, 1, "Lunar Client", launcherPath, now),
            new(20, 1, "Code", Path.Combine("C:\\", "Programs", "Code", "Code.exe"), now),
            new(30, 10, "javaw", Path.Combine("C:\\", "Java", "javaw.exe"), now)
        ]);
        var windows = new TestWindows([
            new((nint)100, 10, true, "Chrome_WidgetWin_1", "Lunar Client"),
            new((nint)200, 20, true, "Chrome_WidgetWin_1", "Lunar Client notes"),
            new((nint)300, 30, true, "LWJGL", "Minecraft 1.8.9")
        ]);
        var service = new LunarBackgroundLaunchService(
            Identity(10, launcherPath, now),
            processes,
            windows);

        service.HideOwnedWindows();

        Assert.Contains((nint)100, windows.Hidden);
        Assert.DoesNotContain((nint)200, windows.Hidden);
        Assert.DoesNotContain((nint)300, windows.Hidden);
    }

    [Fact]
    public void ShowLunarStopsHidingForThatAttemptAndFocusesCurrentReplacement()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var processes = new MutableProcesses([
            new(20, 1, "Lunar Client", launcherPath, now)
        ]);
        var windows = new TestWindows([
            new((nint)200, 20, false, "Chrome_WidgetWin_1", "Lunar Client")
        ]);
        var events = new FakeWindowEvents();
        var service = new LunarBackgroundLaunchService(
            Identity(10, launcherPath, now),
            processes,
            windows,
            events);
        _ = service.OwnedProcessIds;

        Assert.True(service.ShowLunar());
        windows.SetVisible((nint)200, true);
        events.Raise((nint)200);

        Assert.Equal(LunarLaunchWindowState.UserVisible, service.State);
        Assert.Equal((nint)200, windows.Focused);
        Assert.Empty(windows.Hidden);
    }

    [Fact]
    public void LegacyAbsoluteMetadataMigratesToVisibleStoreAndPreservesExternalSource()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(Path.Combine(temp.Path, "data"));
        paths.EnsureUserDirectories();
        var external = CreateWeaveJar(temp.Path, "external.jar", "external");
        var hash = Hash(external);
        WriteIndex(paths, new PackageIndexRecord
        {
            PackageId = "stable-id",
            DisplayName = "External",
            OriginalFileName = "external.jar",
            ManagedFilePath = external,
            Sha256 = hash,
            FileSize = new FileInfo(external).Length,
            Kind = PackageKind.WeaveMod,
            Identifier = "external",
            Enabled = true,
            ImportedAtUtc = DateTimeOffset.UtcNow
        });

        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        var package = Assert.Single(library.Packages);

        Assert.Equal("stable-id", package.PackageId);
        Assert.StartsWith(paths.WeavePackagesDirectory, package.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(external));
        Assert.Equal(hash, Hash(package.FullPath));
        Assert.DoesNotContain(external, File.ReadAllText(paths.PackageIndexPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HiddenLegacyPathCannotBeResolvedForLaunch()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(Path.Combine(temp.Path, "data"));
        var hidden = CreateWeaveJar(paths.LegacyOriginalsPackagesDirectory, "hidden.jar", "hidden");
        var package = new JarMetadataParser().ParseWeaveMod(hidden);
        package.IsEnabled = true;

        var exception = Assert.Throws<InvalidDataException>(() =>
            new PackageLaunchResolver(new JarMetadataParser(), paths).Resolve([package]));

        Assert.Contains("outside the authoritative category folders", exception.Message);
    }

    [Fact]
    public void LegacyJarCreatedAfterOldMigrationMarkerIsStillMigratedOnNextStartup()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var first = Library(paths);
        Assert.True(File.Exists(paths.PackageLayoutMigrationMarkerPath));
        var hidden = CreateWeaveJar(
            paths.LegacyIncomingPackagesDirectory,
            "late-legacy.jar",
            "late-legacy");
        var hash = Hash(hidden);

        var second = new LocalPackageLibrary(paths, new JarMetadataParser());
        second.Load();
        var migrated = Assert.Single(second.Packages);

        Assert.Equal(hash, migrated.Sha256);
        Assert.StartsWith(paths.WeavePackagesDirectory, migrated.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(hidden));
        Assert.False(Directory.Exists(paths.LegacyIncomingPackagesDirectory));
    }

    [Fact]
    public void DeletingVisibleJarRemovesLibraryAndMetadataAndAllowsZeroPackageLaunch()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var library = Library(paths);
        var imported = library.Import(CreateWeaveJar(temp.Path, "enabled.jar", "enabled")).Package!;
        library.SetEnabled(library.Packages.Single(), true);
        File.Delete(imported.FullPath);

        var result = library.Reconcile();
        var selection = new PackageLaunchResolver(new JarMetadataParser(), paths)
            .Resolve(library.Packages);

        Assert.Equal(1, result.Removed);
        Assert.Equal(1, result.RemovedEnabled);
        Assert.Empty(library.Packages);
        Assert.Empty(selection.WeaveMods);
        Assert.Empty(selection.JavaAgents);
        Assert.DoesNotContain(imported.PackageId, File.ReadAllText(paths.PackageIndexPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenamePreservesIdentityAndMoveIsCorrectedToDetectedCategory()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var library = Library(paths);
        var package = library.Import(CreateWeaveJar(temp.Path, "before.jar", "rename")).Package!;
        var packageId = package.PackageId;
        var renamed = Path.Combine(paths.WeavePackagesDirectory, "after.jar");
        File.Move(package.FullPath, renamed);

        library.Reconcile();
        var afterRename = Assert.Single(library.Packages);
        Assert.Equal(packageId, afterRename.PackageId);
        Assert.Equal("after.jar", afterRename.OriginalFileName);

        var wrongCategory = Path.Combine(paths.AgentPackagesDirectory, "after.jar");
        File.Move(afterRename.FullPath, wrongCategory);
        library.Reconcile();
        var corrected = Assert.Single(library.Packages);

        Assert.Equal(packageId, corrected.PackageId);
        Assert.Equal(PackageKind.WeaveMod, corrected.Kind);
        Assert.StartsWith(paths.WeavePackagesDirectory, corrected.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(wrongCategory));
    }

    [Fact]
    public void ReplacingBytesCreatesNewIdentityWithoutGhostEntry()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var library = Library(paths);
        var original = library.Import(CreateWeaveJar(temp.Path, "replace.jar", "first")).Package!;
        var oldId = original.PackageId;
        CreateWeaveJar(paths.WeavePackagesDirectory, "replace.jar", "second");

        var result = library.Reconcile();
        var replacement = Assert.Single(library.Packages);
        var index = File.ReadAllText(paths.PackageIndexPath);

        Assert.NotEqual(oldId, replacement.PackageId);
        Assert.Equal(1, result.IntegrityFailures);
        Assert.DoesNotContain(oldId, index, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(replacement.PackageId, index, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptMetadataIsPreservedAndRebuiltFromVisibleFilesDisabled()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var first = Library(paths);
        _ = first.Import(CreateWeaveJar(temp.Path, "recover.jar", "recover"));
        first.SetEnabled(first.Packages.Single(), true);
        File.WriteAllText(paths.PackageIndexPath, "{broken");

        var rebuilt = new LocalPackageLibrary(paths, new JarMetadataParser());
        rebuilt.Load();
        var package = Assert.Single(rebuilt.Packages);

        Assert.True(rebuilt.LastReconciliationResult!.MetadataRebuilt);
        Assert.False(package.IsEnabled);
        Assert.Single(Directory.EnumerateFiles(
            paths.PackageMetadataRecoveryDirectory,
            "index-corrupt-*.json",
            SearchOption.TopDirectoryOnly));
        Assert.Contains(package.PackageId, File.ReadAllText(paths.PackageIndexPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateVisibleCopiesAreDeduplicatedAndPackageRootHasOnlyExpectedEntries()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var library = Library(paths);
        var package = library.Import(CreateWeaveJar(temp.Path, "one.jar", "same")).Package!;
        var duplicate = Path.Combine(paths.UnclassifiedPackagesDirectory, "duplicate.jar");
        File.Copy(package.FullPath, duplicate);

        var result = library.Reconcile();
        var rootEntries = Directory.EnumerateFileSystemEntries(paths.PackagesDirectory)
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(1, result.DuplicatesRemoved);
        Assert.Single(library.Packages);
        Assert.Single(Directory.EnumerateFiles(
            paths.PackagesDirectory,
            "*.jar",
            SearchOption.AllDirectories));
        Assert.Equal(
            ["agents", "metadata", "README.txt", "unclassified", "weave"],
            rootEntries);
        Assert.False(Directory.Exists(paths.LegacyOriginalsPackagesDirectory));
        Assert.False(Directory.Exists(paths.LegacyImportedPackagesDirectory));
    }

    private static LocalPackageLibrary Library(AppPaths paths)
    {
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        return library;
    }

    private static LunarLaunchIdentity Identity(
        int pid,
        string launcherPath,
        DateTimeOffset launchUtc) =>
        new(
            launcherPath,
            pid,
            launchUtc,
            launchUtc,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetDirectoryName(launcherPath)!
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFileName(launcherPath),
                Path.GetFileNameWithoutExtension(launcherPath)
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "lunarclient"
            });

    private static void WriteIndex(AppPaths paths, PackageIndexRecord record)
    {
        Directory.CreateDirectory(paths.PackageMetadataDirectory);
        File.WriteAllText(
            paths.PackageIndexPath,
            JsonSerializer.Serialize(
                new PackageIndexDocument { Packages = [record] },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string CreateWeaveJar(string directory, string fileName, string id)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(
                   archive.CreateEntry("weave.mod.json").Open(),
                   new UTF8Encoding(false)))
        {
            writer.Write($$"""{"name":"{{id}}","modId":"{{id}}","version":"1.0","entrypoints":["{{id}}.Main"]}""");
        }
        using var payload = new StreamWriter(
            archive.CreateEntry("payload.txt").Open(),
            new UTF8Encoding(false));
        payload.Write(id);
        return path;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class MutableProcesses(IEnumerable<ProcessTreeEntry> entries) : IProcessTreeSnapshot
    {
        public List<ProcessTreeEntry> Entries { get; } = [.. entries];
        public IReadOnlyList<ProcessTreeEntry> Capture() => Entries.ToArray();
    }

    private sealed class TestWindows(IEnumerable<WindowEntry> entries) : IWindowOperations
    {
        private readonly List<WindowEntry> _entries = [.. entries];
        public int FailuresBeforeHide { get; set; }
        public List<nint> Hidden { get; } = [];
        public List<nint> Restored { get; } = [];
        public nint Focused { get; private set; }
        public IReadOnlyList<WindowEntry> Enumerate() => _entries.ToArray();
        public bool Hide(nint handle)
        {
            Hidden.Add(handle);
            if (FailuresBeforeHide > 0)
            {
                FailuresBeforeHide--;
                return false;
            }
            SetVisible(handle, false);
            return true;
        }
        public bool IsVisible(nint handle) =>
            _entries.FirstOrDefault(entry => entry.Handle == handle)?.IsVisible == true;
        public void Restore(nint handle)
        {
            Restored.Add(handle);
            SetVisible(handle, true);
        }
        public bool Focus(nint handle)
        {
            Focused = handle;
            return true;
        }
        public void Add(WindowEntry entry) => _entries.Add(entry);
        public void SetVisible(nint handle, bool visible)
        {
            var index = _entries.FindIndex(entry => entry.Handle == handle);
            if (index >= 0)
                _entries[index] = _entries[index] with { IsVisible = visible };
        }
    }

    private sealed class FakeWindowEvents : IWindowEventSource
    {
        private Action<nint>? _handler;
        public IDisposable Subscribe(Action<nint> windowCreatedOrShown)
        {
            _handler = windowCreatedOrShown;
            return new CallbackDisposable(() => _handler = null);
        }
        public void Raise(nint handle) => _handler?.Invoke(handle);
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-stage2d-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
