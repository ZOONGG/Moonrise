using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class Stage2CRecoveryTests
{
    [Fact]
    public async Task LateElectronWindow_AndReshownWindow_AreHidden()
    {
        var processes = new MutableProcessTree([new(10, 1, "Lunar Client")]);
        var windows = new MutableWindows([]);
        var events = new FakeWindowEvents();
        var service = new LunarBackgroundLaunchService(10, processes, windows, events);
        using var cancellation = new CancellationTokenSource();
        var watch = service.WatchAsync(cancellation.Token);

        windows.Entries.Add(new WindowEntry((nint)101, 10, true));
        events.Raise((nint)101);
        windows.SetVisible((nint)101, true);
        events.Raise((nint)101);

        Assert.Equal([(nint)101, (nint)101], windows.Hidden);
        cancellation.Cancel();
        await watch;
    }

    [Fact]
    public async Task NewLunarChildWindow_IsTracked_WhileJavaAndUnrelatedWindowsAreIgnored()
    {
        var processes = new MutableProcessTree([
            new(10, 1, "Lunar Client"),
            new(20, 1, "unrelated")
        ]);
        var windows = new MutableWindows([
            new((nint)200, 20, true)
        ]);
        var events = new FakeWindowEvents();
        var service = new LunarBackgroundLaunchService(10, processes, windows, events);
        using var cancellation = new CancellationTokenSource();
        var watch = service.WatchAsync(cancellation.Token);

        processes.Entries.Add(new ProcessTreeEntry(11, 10, "Lunar Client"));
        processes.Entries.Add(new ProcessTreeEntry(12, 11, "javaw"));
        windows.Entries.Add(new WindowEntry((nint)110, 11, true));
        windows.Entries.Add(new WindowEntry((nint)120, 12, true));
        events.Raise((nint)110);
        events.Raise((nint)120);
        events.Raise((nint)200);

        Assert.Contains((nint)110, windows.Hidden);
        Assert.DoesNotContain((nint)120, windows.Hidden);
        Assert.DoesNotContain((nint)200, windows.Hidden);
        cancellation.Cancel();
        await watch;
    }

    [Fact]
    public async Task ShowLunar_ChangesStateAndPreventsImmediateRehide()
    {
        var processes = new MutableProcessTree([new(10, 1, "Lunar Client")]);
        var windows = new MutableWindows([new((nint)100, 10, true)]);
        var events = new FakeWindowEvents();
        var service = new LunarBackgroundLaunchService(10, processes, windows, events);
        using var cancellation = new CancellationTokenSource();
        var watch = service.WatchAsync(cancellation.Token);
        var hiddenBeforeShow = windows.Hidden.Count;

        Assert.True(service.ShowLunar());
        events.Raise((nint)100);

        Assert.Equal(LunarLaunchWindowState.UserVisible, service.State);
        Assert.Equal(hiddenBeforeShow, windows.Hidden.Count);
        Assert.Equal((nint)100, windows.Focused);
        await watch;
    }

    [Fact]
    public void NewLaunchStartsHidden_AndRequiredInteractionRevealsLunar()
    {
        var processes = new MutableProcessTree([new(10, 1, "Lunar Client")]);
        var windows = new MutableWindows([new((nint)100, 10, false)]);
        var first = new LunarBackgroundLaunchService(10, processes, windows);
        first.ShowLunar();
        var next = new LunarBackgroundLaunchService(10, processes, windows);

        Assert.Equal(LunarLaunchWindowState.BackgroundHidden, next.State);
        Assert.True(next.RequireInteraction());
        Assert.Equal(LunarLaunchWindowState.InteractionRequired, next.State);
        Assert.Contains((nint)100, windows.Restored);
    }

    [Fact]
    public async Task LauncherExitIsDetectedWithoutClosingOrTerminatingIt()
    {
        var processes = new MutableProcessTree([new(10, 1, "Lunar Client")]);
        var service = new LunarBackgroundLaunchService(
            10,
            processes,
            new MutableWindows([]));
        var watch = service.WatchAsync(CancellationToken.None);

        processes.Entries.Clear();
        await watch.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(LunarLaunchWindowState.LauncherExited, service.State);
    }

    [Fact]
    public void MinecraftDetectionKeepsLauncherAliveAndHidden()
    {
        var processes = new MutableProcessTree([
            new(10, 1, "Lunar Client"),
            new(12, 10, "javaw")
        ]);
        var windows = new MutableWindows([
            new((nint)100, 10, true),
            new((nint)120, 12, true)
        ]);
        var service = new LunarBackgroundLaunchService(10, processes, windows);

        service.MarkMinecraftDetected();

        Assert.Equal(LunarLaunchWindowState.MinecraftDetected, service.State);
        Assert.True(service.IsLauncherTreeRunning());
        Assert.Contains((nint)100, windows.Hidden);
        Assert.DoesNotContain((nint)120, windows.Hidden);
        Assert.Single(processes.Entries, entry => entry.ProcessId == 10);
    }

    [Fact]
    public void FinalLayoutMigratesEveryLegacyDirectoryWithoutLosingJarBytes()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var legacyRoots = new[]
        {
            paths.LegacyAddPackagesDirectory,
            paths.LegacyImportedPackagesDirectory,
            paths.LegacyIncomingPackagesDirectory,
            paths.LegacyInstalledPackagesDirectory,
            paths.LegacyMoonriseOwnedPackagesDirectory,
            paths.LegacyOriginalsPackagesDirectory
        };
        var expectedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < legacyRoots.Length; index++)
        {
            var jar = CreateWeaveJar(legacyRoots[index], $"legacy-{index}.jar", $"legacy-{index}");
            expectedHashes.Add(Hash(jar));
        }
        Directory.CreateDirectory(paths.LegacyAddPackagesDirectory);
        File.WriteAllText(Path.Combine(paths.LegacyAddPackagesDirectory, "legacy-note.txt"), "preserve me");

        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();

        Assert.True(expectedHashes.SetEquals(library.Packages.Select(package => package.Sha256)));
        Assert.All(library.Packages, package => Assert.False(package.IsEnabled));
        Assert.Equal(
            ["agents", "metadata", "unclassified", "weave"],
            Directory.EnumerateDirectories(paths.PackagesDirectory)
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.True(File.Exists(paths.PackageLayoutMigrationMarkerPath));
        Assert.Single(Directory.EnumerateFiles(
            paths.LegacyPreservedMetadataDirectory,
            "legacy-note.txt",
            SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InvalidLegacyJarIsPreservedDisabledAndNotReportedAgainByFolderScan()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        Directory.CreateDirectory(paths.LegacyIncomingPackagesDirectory);
        var invalid = Path.Combine(paths.LegacyIncomingPackagesDirectory, "broken.jar");
        File.WriteAllBytes(invalid, [1, 2, 3, 4]);
        var expected = Hash(invalid);
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());

        library.Load();
        var package = Assert.Single(library.Packages);
        var scan = await library.ScanCategoryFoldersAsync(TimeSpan.Zero);

        Assert.Equal(PackageKind.Unclassified, package.Kind);
        Assert.False(package.IsEnabled);
        Assert.Equal(expected, Hash(package.FullPath));
        Assert.Contains("legacy-invalid", Path.GetFileName(package.FullPath), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, scan.Invalid);
        Assert.Equal(1, scan.AlreadyPresent);
    }

    [Fact]
    public async Task DirectCategoryCopiesAreIndexed_AndMismatchMovesByteForByte()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        var weave = CreateWeaveJar(paths.WeavePackagesDirectory, "weave.jar", "weave-direct");
        var agent = CreateAgentJar(paths.WeavePackagesDirectory, "agent.jar", "sample.Agent");
        var weaveHash = Hash(weave);
        var agentHash = Hash(agent);

        var summary = await library.ScanCategoryFoldersAsync(TimeSpan.Zero);

        Assert.Equal(2, summary.Imported);
        Assert.Equal(2, library.Packages.Count);
        Assert.Equal(weaveHash, Hash(weave));
        var movedAgent = library.Packages.Single(package => package.Kind == PackageKind.JavaAgent);
        Assert.StartsWith(paths.AgentPackagesDirectory, movedAgent.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(agentHash, Hash(movedAgent.FullPath));
        Assert.False(File.Exists(agent));
    }

    [Fact]
    public void AddJarAndDragDropUseTheSameCategoryStorageWithoutChangingBytes()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(Path.Combine(temp.Path, "data"));
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        var addSource = CreateWeaveJar(temp.Path, "add.jar", "add");
        var dragSource = CreateAgentJar(temp.Path, "drag.jar", "drag.Agent");
        var addBytes = File.ReadAllBytes(addSource);
        var dragBytes = File.ReadAllBytes(dragSource);

        var added = library.Import(addSource);
        var dragged = library.ImportMany([dragSource]);

        Assert.Equal(PackageImportStatus.Imported, added.Status);
        Assert.Equal(1, dragged.Imported);
        Assert.StartsWith(paths.WeavePackagesDirectory, added.Package!.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(
            paths.AgentPackagesDirectory,
            library.Packages.Single(package => package.Kind == PackageKind.JavaAgent).FullPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(addBytes, File.ReadAllBytes(addSource));
        Assert.Equal(dragBytes, File.ReadAllBytes(dragSource));
        Assert.Equal(addBytes, File.ReadAllBytes(added.Package.FullPath));
    }

    [Fact]
    public void LibraryFolderButtonIsTabAwareAndThereIsNoRescanCommand()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml.cs"));

        Assert.Contains("_paths.PackagesDirectory", code, StringComparison.Ordinal);
        Assert.Contains("_paths.WeavePackagesDirectory", code, StringComparison.Ordinal);
        Assert.Contains("_paths.AgentPackagesDirectory", code, StringComparison.Ordinal);
        Assert.Contains("_paths.UnclassifiedPackagesDirectory", code, StringComparison.Ordinal);
        Assert.Contains("Открыть папку пакетов", code, StringComparison.Ordinal);
        Assert.Contains("Открыть папку Weave-модов", code, StringComparison.Ordinal);
        Assert.Contains("Открыть папку Java-агентов", code, StringComparison.Ordinal);
        Assert.Contains("Открыть папку без типа", code, StringComparison.Ordinal);
        Assert.Contains("Open package folder", code, StringComparison.Ordinal);
        Assert.Contains("Open Weave mods folder", code, StringComparison.Ordinal);
        Assert.Contains("Open Java agents folder", code, StringComparison.Ordinal);
        Assert.Contains("Open unclassified folder", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Rescan", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedLaunchCardHasExactlyOneContextualReportAction()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml"));
        var start = xaml.IndexOf("<StackPanel x:Name=\"CrashActionsPanel\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</StackPanel>", start, StringComparison.Ordinal);
        var panel = xaml[start..end];

        Assert.Equal(1, Count(panel, "<Button "));
        Assert.Contains("OpenCrashReportButton", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Launch without packages", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LaunchButton", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void CrashReportCapturesNewestLcluIdAndRedactsSensitiveValues()
    {
        using var temp = new TestDirectory();
        var logs = Directory.CreateDirectory(Path.Combine(temp.Path, "logs")).FullName;
        var started = DateTimeOffset.Parse("2001-02-03T04:05:00Z");
        var failed = started.AddMinutes(2);
        var older = Path.Combine(logs, "lunar-old.log");
        var newer = Path.Combine(logs, "lunar-new.log");
        File.WriteAllText(older, "LCLU-OLD123");
        File.WriteAllText(newer,
            "Crash: LCLU-FPYJXWEIIVVU\naccess_token=secret-value\naccountName=PlayerOne");
        File.SetLastWriteTimeUtc(older, started.AddSeconds(10).UtcDateTime);
        File.SetLastWriteTimeUtc(newer, started.AddSeconds(20).UtcDateTime);
        var launchReport = Path.Combine(temp.Path, "launch-report.json");
        File.WriteAllText(launchReport, """{"stage":"java","refreshToken":"hidden"}""");
        File.SetLastWriteTimeUtc(launchReport, started.AddSeconds(30).UtcDateTime);
        var package = new PackageInfo
        {
            PackageId = "stable",
            DisplayName = "Synthetic package",
            FileName = "synthetic.jar",
            OriginalFileName = "synthetic.jar",
            Identifier = "synthetic",
            FullPath = Path.Combine(temp.Path, "synthetic.jar"),
            Kind = PackageKind.WeaveMod,
            Sha256 = new string('A', 64)
        };

        var result = new PackageCrashDiagnosticsService().Capture(
            new PackageCrashCaptureRequest(
                started,
                failed,
                "java-exited-before-usable-window",
                -1,
                [package],
                Path.Combine(temp.Path, "weave-loader.jar"),
                new string('B', 64),
                launchReport,
                "success",
                ["authorization: Bearer private"],
                [logs],
                MinecraftVersion: "1.8.9",
                LanguageCode: "en",
                WeaveLoaderVersion: "test-version"),
            Path.Combine(temp.Path, "crashes"));

        var summary = File.ReadAllText(result.SummaryPath);
        var report = File.ReadAllText(result.ReportPath);
        var allText = string.Join(
            "\n",
            Directory.EnumerateFiles(result.DirectoryPath).Select(File.ReadAllText));
        Assert.Equal("LCLU-FPYJXWEIIVVU", result.LunarCrashIdentifier);
        Assert.Contains("LCLU-FPYJXWEIIVVU", summary, StringComparison.Ordinal);
        Assert.Contains("LCLU-FPYJXWEIIVVU", report, StringComparison.Ordinal);
        Assert.Contains("Minecraft version: 1.8.9", report, StringComparison.Ordinal);
        Assert.Contains("Java exit code: -1", report, StringComparison.Ordinal);
        Assert.Contains("test-version", report, StringComparison.Ordinal);
        Assert.Contains("Compatibility of the enabled packages", report, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerOne", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer private", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshToken\":\"hidden", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportActionUsesExactGeneratedPath_AndSuccessClearsStaleActions()
    {
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "Moonrise", "MainWindow.xaml.cs"));

        Assert.Contains("_lastCrashReportPath = result.ReportPath", source, StringComparison.Ordinal);
        Assert.Contains("FileName = _lastCrashReportPath", source, StringComparison.Ordinal);
        Assert.Contains("_lastCrashReportPath = null", source, StringComparison.Ordinal);
        Assert.Contains("CrashActionsPanel.Visibility = Visibility.Collapsed", source, StringComparison.Ordinal);
        Assert.Contains("OpenDiagnosticsButton.Visibility = Visibility.Collapsed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroPackageAndAllPackageResolutionRemainUnchanged()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(Path.Combine(temp.Path, "data"));
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        _ = library.Import(CreateWeaveJar(temp.Path, "enabled.jar", "enabled"));
        library.SetEnabled(library.Packages.Single(), true);
        var resolver = new PackageLaunchResolver(new JarMetadataParser());

        var zero = resolver.Resolve(library.Packages, excludeAll: true);
        var normal = resolver.Resolve(library.Packages);

        Assert.Empty(zero.WeaveMods);
        Assert.Empty(zero.JavaAgents);
        Assert.Single(normal.WeaveMods);
        Assert.Empty(normal.JavaAgents);
        Assert.True(library.Packages.Single().IsEnabled);
    }

    private static int Count(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }
        return count;
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
        using (var writer = new StreamWriter(
                   archive.CreateEntry("payload.txt").Open(),
                   new UTF8Encoding(false)))
        {
            writer.Write(id);
        }
        return path;
    }

    private static string CreateAgentJar(string directory, string fileName, string className)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(
                   archive.CreateEntry("META-INF/MANIFEST.MF").Open(),
                   new UTF8Encoding(false)))
        {
            writer.Write($"Manifest-Version: 1.0\r\nPremain-Class: {className}\r\n");
        }
        using (var writer = new StreamWriter(
                   archive.CreateEntry("payload.txt").Open(),
                   new UTF8Encoding(false)))
        {
            writer.Write(className);
        }
        return path;
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

    private sealed class MutableProcessTree(
        IEnumerable<ProcessTreeEntry> entries) : IProcessTreeSnapshot
    {
        public List<ProcessTreeEntry> Entries { get; } = [.. entries];
        public IReadOnlyList<ProcessTreeEntry> Capture() => Entries.ToArray();
    }

    private sealed class MutableWindows(
        IEnumerable<WindowEntry> entries) : IWindowOperations
    {
        public List<WindowEntry> Entries { get; } = [.. entries];
        public List<nint> Hidden { get; } = [];
        public List<nint> Restored { get; } = [];
        public nint Focused { get; private set; }
        public IReadOnlyList<WindowEntry> Enumerate() => Entries.ToArray();
        public bool Hide(nint handle)
        {
            Hidden.Add(handle);
            SetVisible(handle, false);
            return true;
        }
        public bool IsVisible(nint handle) =>
            Entries.FirstOrDefault(entry => entry.Handle == handle)?.IsVisible == true;
        public void Restore(nint handle)
        {
            Restored.Add(handle);
            SetVisible(handle, true);
        }
        public void SetVisible(nint handle, bool visible)
        {
            var index = Entries.FindIndex(entry => entry.Handle == handle);
            if (index >= 0)
                Entries[index] = Entries[index] with { IsVisible = visible };
        }
        public bool Focus(nint handle)
        {
            Focused = handle;
            return true;
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

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-stage2c-tests-{Guid.NewGuid():N}");
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
