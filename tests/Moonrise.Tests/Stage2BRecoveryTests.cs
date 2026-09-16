using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class Stage2BRecoveryTests
{
    [Fact]
    public void OfficialLunarStart_UsesBackgroundProcessSettings()
    {
        var startInfo = OfficialLunarStartInfoFactory.Create(
            Path.Combine("C:", "Program Files", "Lunar Client", "Lunar Client.exe"),
            background: true);

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(System.Diagnostics.ProcessWindowStyle.Hidden, startInfo.WindowStyle);
    }

    [Fact]
    public void OfficialLunarStart_IncludesLaunchDeepLinkInFirstProcess()
    {
        var startInfo = OfficialLunarStartInfoFactory.Create(
            Path.Combine("C:", "Program Files", "Lunar Client", "Lunar Client.exe"),
            background: true,
            "lunarclient://launch");

        Assert.Equal(["lunarclient://launch"], startInfo.ArgumentList);
    }

    [Fact]
    public void MainLaunch_SendsDeepLinkOnlyAfterProfileReadiness()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "Moonrise",
            "MainWindow.xaml.cs"));

        var launchFlowStart = source.IndexOf(
            "var confirmed = await WaitForProfileWithLauncherAsync(",
            StringComparison.Ordinal);
        var dispatch = source.IndexOf(
            "await DispatchLaunchToExistingLunarAsync(launcherPath, token)",
            launchFlowStart,
            StringComparison.Ordinal);
        var restore = source.IndexOf(
            "profileSelection.Restore()",
            dispatch,
            StringComparison.Ordinal);

        Assert.True(launchFlowStart >= 0);
        Assert.True(dispatch > launchFlowStart);
        Assert.True(restore > dispatch);
        Assert.Contains("launcherArgument: null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundLaunch_HidesOnlyLunarProcessTreeWindows()
    {
        var processes = new FakeProcessTree([
            new(10, 1, "Lunar Client"),
            new(11, 10, "Lunar Client"),
            new(12, 11, "javaw"),
            new(20, 1, "unrelated")
        ]);
        var windows = new FakeWindows([
            new((nint)100, 10, true),
            new((nint)110, 11, true),
            new((nint)120, 12, true),
            new((nint)200, 20, true)
        ]);
        var service = new LunarBackgroundLaunchService(10, processes, windows);

        var hidden = service.HideOwnedWindows();

        Assert.Equal(2, hidden);
        Assert.Equal([(nint)100, (nint)110], windows.Hidden);
        Assert.DoesNotContain((nint)120, windows.Hidden);
        Assert.DoesNotContain((nint)200, windows.Hidden);
    }

    [Fact]
    public void ShowLunar_RestoresAndFocusesTheRootLauncherWindow()
    {
        var processes = new FakeProcessTree([
            new(10, 1, "Lunar Client"),
            new(11, 10, "Lunar Client")
        ]);
        var windows = new FakeWindows([
            new((nint)110, 11, false),
            new((nint)100, 10, false)
        ]);
        var service = new LunarBackgroundLaunchService(10, processes, windows);

        Assert.True(service.RevealAndFocus());
        Assert.Equal([(nint)100, (nint)110], windows.Restored.Order().ToArray());
        Assert.Equal((nint)100, windows.Focused);
    }

    [Fact]
    public void TimeoutPath_RevealsLunarAndOffersDiagnostics()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Moonrise", "MainWindow.xaml.cs"));
        var timeoutCatch = source[source.IndexOf("catch (TimeoutException", StringComparison.Ordinal)..];
        timeoutCatch = timeoutCatch[..timeoutCatch.IndexOf("catch (OperationCanceledException", StringComparison.Ordinal)];

        Assert.Contains(
            "RevealActiveLunar(force: true, interactionRequired: true)",
            timeoutCatch,
            StringComparison.Ordinal);
        Assert.Contains("OpenDiagnosticsButton.Visibility = Visibility.Visible", timeoutCatch, StringComparison.Ordinal);
        Assert.Contains("Требуется действие в Lunar", timeoutCatch, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageFolderUi_UsesRootAndHasNoVisibleRescanCommand()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"OpenFolderButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open folder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Add JAR", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Rescan", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "OpenPackageFolderButton_Click(object sender, RoutedEventArgs e) =>",
            code,
            StringComparison.Ordinal);
        Assert.Contains("OpenFolder(GetSelectedPackageFolder())", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageLayout_CreatesReadmeAndExpectedFolders()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();

        Assert.True(Directory.Exists(paths.WeavePackagesDirectory));
        Assert.True(Directory.Exists(paths.AgentPackagesDirectory));
        Assert.True(Directory.Exists(paths.UnclassifiedPackagesDirectory));
        Assert.True(Directory.Exists(paths.PackageMetadataDirectory));
        Assert.False(Directory.Exists(paths.LegacyAddPackagesDirectory));
        Assert.False(Directory.Exists(paths.LegacyInstalledPackagesDirectory));
        var readme = File.ReadAllText(paths.PackageReadmePath);
        Assert.Contains("Копируйте Weave-моды в папку weave", readme, StringComparison.Ordinal);
        Assert.Contains("Copy Java agents into the agents folder", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomingMigration_MovesVerifiedJarToAddExactlyOnce()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        Directory.CreateDirectory(paths.LegacyIncomingPackagesDirectory);
        var source = CreateWeaveJar(paths.LegacyIncomingPackagesDirectory, "Pending Mod.jar", "pending");
        var expected = Hash(source);

        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        var second = library.MigratePackageLayoutOnce();

        var migrated = Assert.Single(Directory.EnumerateFiles(paths.WeavePackagesDirectory, "*.jar"));
        Assert.Equal(expected, Hash(migrated));
        Assert.False(File.Exists(source));
        Assert.True(second.AlreadyComplete);
        Assert.True(File.Exists(paths.PackageLayoutMigrationMarkerPath));
    }

    [Fact]
    public void FriendlyInstalledFiles_UseShortHashOnlyForRealNameCollision()
    {
        using var temp = new TemporaryDirectory();
        var firstRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "one")).FullName;
        var secondRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "two")).FullName;
        var first = CreateWeaveJar(firstRoot, "Stormy-1.0.jar", "stormy-one");
        var second = CreateWeaveJar(secondRoot, "Stormy-1.0.jar", "stormy-two");
        var library = CreateLibrary(temp.Path);

        library.Import(first);
        library.Import(second);

        var names = library.Packages.Select(package => Path.GetFileName(package.FullPath)).Order().ToArray();
        Assert.Contains("Stormy-1.0.jar", names);
        var collision = Assert.Single(
            names,
            name => name.StartsWith("Stormy-1.0--", StringComparison.Ordinal));
        Assert.Matches(@"^Stormy-1\.0--[0-9a-f]{8}\.jar$", collision);
        Assert.Equal(Hash(first), Hash(library.Packages.Single(item => Path.GetFileName(item.FullPath) == "Stormy-1.0.jar").FullPath));
        Assert.Equal(Hash(second), Hash(library.Packages.Single(item => Path.GetFileName(item.FullPath) == collision).FullPath));
    }

    [Fact]
    public void LayoutMigration_PreservesIdStateCompatibilityAndRecoversAfterVerifiedCopy()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        Directory.CreateDirectory(paths.LegacyOriginalsPackagesDirectory);
        var original = CreateWeaveJar(paths.LegacyOriginalsPackagesDirectory, "legacy-hash.jar", "legacy");
        var hash = Hash(original);
        var destination = Path.Combine(paths.InstalledWeavePackagesDirectory, "Legacy Friendly.jar");
        File.Copy(original, destination);
        var importedAt = DateTimeOffset.Parse("2026-07-01T10:00:00Z");
        WriteIndex(paths, new PackageIndexRecord
        {
            PackageId = "stable-package-id",
            DisplayName = "Legacy",
            OriginalFileName = "Legacy Friendly.jar",
            ManagedFilePath = Path.GetRelativePath(paths.RootDirectory, original),
            Sha256 = hash,
            FileSize = new FileInfo(original).Length,
            Kind = PackageKind.WeaveMod,
            Identifier = "legacy",
            Enabled = true,
            CompatibilityStatus = "untested",
            ImportedAtUtc = importedAt
        });

        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        var migrated = Assert.Single(library.Packages);

        Assert.Equal("stable-package-id", migrated.PackageId);
        Assert.True(migrated.IsEnabled);
        Assert.Equal("untested", migrated.CompatibilityStatus);
        Assert.Equal(importedAt, migrated.ImportedAtUtc);
        Assert.Equal(destination, migrated.FullPath, ignoreCase: true);
        Assert.Equal(hash, Hash(migrated.FullPath));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public void SameHash_RemainsDeduplicatedAndIdStableAfterReload()
    {
        using var temp = new TemporaryDirectory();
        var first = CreateWeaveJar(temp.Path, "Friendly.jar", "same-content");
        var second = Path.Combine(temp.Path, "renamed.jar");
        File.Copy(first, second);
        var paths = new AppPaths(Path.Combine(temp.Path, "data"));
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();

        var imported = library.Import(first).Package!;
        var duplicate = library.Import(second);
        var reload = new LocalPackageLibrary(paths, new JarMetadataParser());
        reload.Load();

        Assert.Equal(PackageImportStatus.AlreadyImported, duplicate.Status);
        Assert.Single(reload.Packages);
        Assert.Equal(imported.PackageId, reload.Packages[0].PackageId);
        Assert.Single(Directory.EnumerateFiles(paths.PackagesDirectory, "*.jar", SearchOption.AllDirectories));
    }

    [Fact]
    public void TemporaryCleanup_PreservesInstalledPackagesAndRecentLaunches()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        var installed = CreateWeaveJar(paths.InstalledWeavePackagesDirectory, "keep.jar", "keep");
        File.WriteAllText(paths.PackageIndexPath, "{}");
        var recentLaunch = Directory.CreateDirectory(Path.Combine(paths.TempDirectory, "launch-recent")).FullName;
        var oldLaunch = Directory.CreateDirectory(Path.Combine(paths.TempDirectory, "launch-old")).FullName;
        Directory.SetLastWriteTimeUtc(oldLaunch, DateTime.UtcNow.AddHours(-2));
        var abandoned = Path.Combine(paths.WeavePackagesDirectory, "old.importing");
        File.WriteAllText(abandoned, "partial");
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));

        var result = new MoonriseStorageService(paths).ClearTemporaryFiles(DateTimeOffset.UtcNow);

        Assert.True(File.Exists(installed));
        Assert.True(File.Exists(paths.PackageIndexPath));
        Assert.True(Directory.Exists(recentLaunch));
        Assert.False(Directory.Exists(oldLaunch));
        Assert.False(File.Exists(abandoned));
        Assert.True(result.FilesDeleted >= 1);
        Assert.True(result.DirectoriesDeleted >= 1);
    }

    [Fact]
    public void PackageCrashBundle_IsBoundedCompleteAndSanitized()
    {
        using var temp = new TemporaryDirectory();
        var logs = Directory.CreateDirectory(Path.Combine(temp.Path, "launch-logs")).FullName;
        var now = DateTimeOffset.UtcNow;
        WriteLaunchLog(Path.Combine(logs, "weave.log"),
            new string('x', PackageCrashDiagnosticsService.MaximumCapturedTextBytes * 2) +
            "\naccess_token=secret-value\nserver=203.0.113.10:25565\nusername=PlayerOne");
        WriteLaunchLog(Path.Combine(logs, "mixin.log"), "Mixin failure");
        WriteLaunchLog(Path.Combine(logs, "lunar-main.log"), "Crash ID: lunar-crash-12345");
        WriteLaunchLog(Path.Combine(logs, "java-stdout.log"), "authorization: Bearer very-secret");
        var report = Path.Combine(temp.Path, "launch-report.json");
        File.WriteAllText(report, """{"session_token":"secret","stage":"java"}""");
        File.SetLastWriteTimeUtc(report, now.UtcDateTime);
        var package = new PackageInfo
        {
            PackageId = "sha256:abc",
            FileName = "Veyra.jar",
            OriginalFileName = "Veyra.jar",
            DisplayName = "Veyra",
            Identifier = "veyra",
            FullPath = Path.Combine(temp.Path, "Veyra.jar"),
            Kind = PackageKind.WeaveMod,
            Sha256 = new string('A', 64)
        };

        var result = new PackageCrashDiagnosticsService().Capture(
            new PackageCrashCaptureRequest(
                now.AddMinutes(-1),
                now,
                "java-exited-before-usable-window",
                -1,
                [package],
                Path.Combine(temp.Path, "weave-loader.jar"),
                new string('B', 64),
                report,
                "success",
                ["token=diagnostic-secret", "Connected to https://private.example.test:443"],
                [logs]),
            Path.Combine(temp.Path, "crashes"));

        Assert.True(result.TotalBytes <= PackageCrashDiagnosticsService.MaximumBundleBytes);
        Assert.Equal("lunar-crash-12345", result.LunarCrashIdentifier);
        foreach (var required in new[]
                 {
                     "crash-summary.json",
                     "enabled-packages.json",
                     "relevant-log-paths.txt",
                     "recent-weave-log.txt",
                     "recent-mixin-log.txt",
                     "recent-lunar-log.txt",
                     "recent-java-output.txt",
                     "moonrise-launch-report.json",
                     "moonrise-diagnostics.txt"
                 })
        {
            Assert.True(File.Exists(Path.Combine(result.DirectoryPath, required)), required);
        }
        var allText = string.Join(
            "\n",
            Directory.EnumerateFiles(result.DirectoryPath).Select(File.ReadAllText));
        Assert.Contains(new string('A', 64), allText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerOne", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.10", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("private.example.test", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchWithoutPackages_DoesNotPersistentlyDisableEnabledPackages()
    {
        var package = new PackageInfo
        {
            PackageId = "stable",
            FileName = "enabled.jar",
            OriginalFileName = "enabled.jar",
            DisplayName = "Enabled",
            Identifier = "enabled",
            FullPath = "unused-for-excluded-launch.jar",
            Kind = PackageKind.WeaveMod,
            Sha256 = new string('C', 64),
            IsEnabled = true
        };

        var selection = new PackageLaunchResolver(new JarMetadataParser()).Resolve([package], excludeAll: true);

        Assert.Empty(selection.WeaveMods);
        Assert.Empty(selection.JavaAgents);
        Assert.True(package.IsEnabled);
    }

    [Fact]
    public void RuntimeSource_HasNoAbsoluteMoonriseDevelopmentPaths()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "src", "Moonrise");
        var forbidden = new[]
        {
            @"D:\A\Python\MoonRise",
            @"D:\A\Python\Moonrise-Catalog",
            @"D:\A\Python\Veyra",
            @"D:\A\Python\Moonrise-TestAssets",
            @"D:\A\Python\moonrise-recovery-audit"
        };
        var sources = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml")
            .ToArray();

        foreach (var path in sources)
        {
            var text = File.ReadAllText(path);
            Assert.All(forbidden, value => Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AppPaths_RemainValidAfterRepositoryRelocation()
    {
        using var temp = new TemporaryDirectory();
        var firstInstall = Directory.CreateDirectory(Path.Combine(temp.Path, "first", "app")).FullName;
        var relocatedInstall = Directory.CreateDirectory(Path.Combine(temp.Path, "relocated", "app")).FullName;
        var local = Directory.CreateDirectory(Path.Combine(temp.Path, "local")).FullName;

        var first = AppPaths.Discover(firstInstall, local);
        var relocated = AppPaths.Discover(relocatedInstall, local);

        Assert.Equal(first.RootDirectory, relocated.RootDirectory);
        Assert.Equal(Path.Combine(local, "Moonrise"), relocated.RootDirectory);
        Assert.StartsWith(relocatedInstall, relocated.NativeBridgePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(firstInstall, relocated.NativeBridgePath, StringComparison.OrdinalIgnoreCase);
    }

    private static LocalPackageLibrary CreateLibrary(string root)
    {
        var library = new LocalPackageLibrary(new AppPaths(root), new JarMetadataParser());
        library.Load();
        return library;
    }

    private static void WriteIndex(AppPaths paths, PackageIndexRecord record)
    {
        var document = new PackageIndexDocument { Packages = [record] };
        File.WriteAllText(
            paths.PackageIndexPath,
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
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
            writer.Write($$"""{"name":"{{id}}","modId":"{{id}}","version":"1.0.0","entrypoints":["{{id}}.Main"]}""");
        }
        using (var writer = new StreamWriter(
                   archive.CreateEntry("payload.txt").Open(),
                   new UTF8Encoding(false)))
        {
            writer.Write(id);
        }
        return path;
    }

    private static void WriteLaunchLog(string path, string text)
    {
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("MOONRISE_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) &&
            File.Exists(Path.Combine(configured, "Moonrise.sln")))
        {
            return Path.GetFullPath(configured);
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Moonrise.sln")))
                    return current.FullName;
                current = current.Parent;
            }
        }
        throw new DirectoryNotFoundException("Moonrise.sln was not found.");
    }

    private sealed class FakeProcessTree(IReadOnlyList<ProcessTreeEntry> entries) : IProcessTreeSnapshot
    {
        public IReadOnlyList<ProcessTreeEntry> Capture() => entries;
    }

    private sealed class FakeWindows(IReadOnlyList<WindowEntry> entries) : IWindowOperations
    {
        private readonly HashSet<nint> _hiddenHandles = [];
        public List<nint> Hidden { get; } = [];
        public List<nint> Restored { get; } = [];
        public nint Focused { get; private set; }
        public IReadOnlyList<WindowEntry> Enumerate() => entries
            .Select(entry => entry with { IsVisible = entry.IsVisible && !_hiddenHandles.Contains(entry.Handle) })
            .ToArray();
        public bool Hide(nint handle)
        {
            Hidden.Add(handle);
            _hiddenHandles.Add(handle);
            return true;
        }
        public bool IsVisible(nint handle) =>
            Enumerate().FirstOrDefault(entry => entry.Handle == handle)?.IsVisible == true;
        public void Restore(nint handle)
        {
            Restored.Add(handle);
            _hiddenHandles.Remove(handle);
        }
        public bool Focus(nint handle)
        {
            Focused = handle;
            return true;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-stage2b-tests-{Guid.NewGuid():N}");
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
