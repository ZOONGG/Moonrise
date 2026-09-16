using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LocalPackageLibraryTests
{
    [Fact]
    public void LaunchResolution_AllowsZeroPackages()
    {
        var selection = new PackageLaunchResolver(new JarMetadataParser()).Resolve([]);
        Assert.Empty(selection.WeaveMods);
        Assert.Empty(selection.JavaAgents);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    [InlineData(2, 2)]
    public void LaunchResolution_SupportsOneMultipleAndMixedPackages(int modCount, int agentCount)
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        for (var index = 0; index < modCount; index++)
            Enable(library, library.Import(CreateWeaveJar(temp.Path, $"mod-{index}.jar", $"mod-{index}")).Package!);
        for (var index = 0; index < agentCount; index++)
            Enable(library, library.Import(CreateAgentJar(temp.Path, $"agent-{index}.jar", $"probe.Agent{index}")).Package!);

        var selection = new PackageLaunchResolver(new JarMetadataParser()).Resolve(library.Packages);

        Assert.Equal(modCount, selection.WeaveMods.Count);
        Assert.Equal(agentCount, selection.JavaAgents.Count);
    }

    [Fact]
    public void LaunchResolution_HasNoVeyraCosmeticsOrFilenameRequirement()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        Enable(library, library.Import(CreateWeaveJar(temp.Path, "anything.jar", "arbitrary")).Package!);
        Enable(library, library.Import(CreateAgentJar(temp.Path, "another-name.jar", "example.ArbitraryAgent")).Package!);

        var selection = new PackageLaunchResolver(new JarMetadataParser()).Resolve(library.Packages);

        Assert.Equal("anything.jar", Assert.Single(selection.WeaveMods).OriginalFileName);
        Assert.Equal("another-name.jar", Assert.Single(selection.JavaAgents).OriginalFileName);
    }

    [Fact]
    public void Import_DetectsValidWeaveAndJavaAgentJars()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);

        var mod = library.Import(CreateWeaveJar(temp.Path, "mod.jar", "sample")).Package!;
        var agent = library.Import(CreateAgentJar(temp.Path, "agent.jar", "sample.Agent")).Package!;

        Assert.Equal(PackageKind.WeaveMod, mod.Kind);
        Assert.Equal(PackageKind.JavaAgent, agent.Kind);
        Assert.False(mod.IsEnabled);
        Assert.False(agent.IsEnabled);
    }

    [Fact]
    public void Import_DetectsAmbiguousAndUnclassifiedJarsAsDisabled()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var ambiguous = Path.Combine(temp.Path, "ambiguous.jar");
        CreateJar(ambiguous, ValidWeaveJson("both"), Manifest("both.Agent"), "both");
        var plain = Path.Combine(temp.Path, "plain.jar");
        CreateJar(plain, null, "Manifest-Version: 1.0\r\n", "plain");

        var both = library.Import(ambiguous).Package!;
        var unclassified = library.Import(plain).Package!;

        Assert.Equal(PackageKind.Ambiguous, both.Kind);
        Assert.Equal(PackageKind.Unclassified, unclassified.Kind);
        Assert.False(both.IsEnabled);
        Assert.False(unclassified.IsEnabled);
    }

    [Fact]
    public void Import_RejectsInvalidArchiveAndMalformedWeaveMetadata()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var invalid = Path.Combine(temp.Path, "invalid.jar");
        File.WriteAllText(invalid, "not a zip");
        var malformed = Path.Combine(temp.Path, "malformed.jar");
        CreateJar(malformed, "{broken", null, "broken");

        Assert.Throws<InvalidDataException>(() => library.Import(invalid));
        var exception = Assert.Throws<InvalidDataException>(() => library.Import(malformed));
        Assert.Contains("weave.mod.json is malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(library.Packages);
    }

    [Fact]
    public void Import_DeduplicatesBySha256AndUsesFriendlyInstalledStorage()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var source = CreateWeaveJar(temp.Path, "first.jar", "duplicate");
        var copy = Path.Combine(temp.Path, "second.jar");
        File.Copy(source, copy);

        var first = library.Import(source);
        var second = library.Import(copy);

        Assert.Equal(PackageImportStatus.Imported, first.Status);
        Assert.Equal(PackageImportStatus.AlreadyImported, second.Status);
        Assert.Single(library.Packages);
        Assert.Equal("first.jar", Path.GetFileName(first.Package!.FullPath));
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(temp.Path, "packages", "weave"),
            "*.jar"));
    }

    [Fact]
    public void Import_SameFilenameWithDifferentHashesCreatesDistinctStableIds()
    {
        using var temp = new TestDirectory();
        var firstDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "one")).FullName;
        var secondDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "two")).FullName;
        var first = CreateWeaveJar(firstDirectory, "same.jar", "one");
        var second = CreateWeaveJar(secondDirectory, "same.jar", "two");
        var library = CreateLibrary(temp.Path);

        library.Import(first);
        library.Import(second);

        Assert.Equal(2, library.Packages.Count);
        Assert.Equal(2, library.Packages.Select(item => item.PackageId).Distinct().Count());
        Assert.All(library.Packages, item => Assert.Equal("same.jar", item.OriginalFileName));
    }

    [Fact]
    public void Import_PreservesSourceAndManagedCopyBytes()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var source = CreateWeaveJar(temp.Path, "immutable.jar", "immutable");
        var originalHash = Hash(source);
        var originalBytes = File.ReadAllBytes(source);

        var package = library.Import(source).Package!;

        Assert.Equal(originalHash, Hash(source));
        Assert.Equal(originalHash, Hash(package.FullPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(source));
        Assert.Equal(originalBytes, File.ReadAllBytes(package.FullPath));
    }

    [Fact]
    public void ImportMany_IsTheMultipleFileDragDropCommandAndReportsEveryResult()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var mod = CreateWeaveJar(temp.Path, "mod.jar", "drag-mod");
        var agent = CreateAgentJar(temp.Path, "agent.jar", "drag.Agent");
        var invalid = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(invalid, "not a jar");

        var summary = library.ImportMany([mod, agent, mod, invalid]);

        Assert.Equal(2, summary.Imported);
        Assert.Equal(1, summary.Invalid);
        Assert.Equal(2, library.Packages.Count);
    }

    [Fact]
    public async Task CategoryScan_IndexesCompleteJarInPlace()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        var incoming = CreateWeaveJar(paths.WeavePackagesDirectory, "incoming.jar", "incoming");

        var summary = await library.ScanIncomingAsync(TimeSpan.FromMilliseconds(10));

        Assert.Equal(1, summary.Imported);
        Assert.True(File.Exists(incoming));
        Assert.Single(library.Packages);
        Assert.Equal(incoming, library.Packages[0].FullPath, ignoreCase: true);
    }

    [Fact]
    public async Task CategoryScan_DebouncesIncompleteCopyAndLeavesItInPlace()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        var incoming = Path.Combine(paths.WeavePackagesDirectory, "copying.jar");
        using var locked = new FileStream(incoming, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        locked.Write([1, 2, 3]);
        locked.Flush();

        var summary = await library.ScanIncomingAsync(TimeSpan.FromMilliseconds(10));

        Assert.Equal(1, summary.Failed);
        Assert.True(File.Exists(incoming));
        Assert.Empty(library.Packages);
    }

    [Fact]
    public void Library_RefreshesImmediatelyAndPersistsAcrossRestart()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var first = new LocalPackageLibrary(paths, new JarMetadataParser());
        first.Load();
        var source = CreateWeaveJar(temp.Path, "persist.jar", "persist");

        first.Import(source);
        Assert.Single(first.Packages);

        var second = new LocalPackageLibrary(paths, new JarMetadataParser());
        second.Load();
        var restored = Assert.Single(second.Packages);
        Assert.Equal(first.Packages[0].PackageId, restored.PackageId);
        Assert.True(File.Exists(paths.PackageIndexPath));
    }

    [Fact]
    public void Library_PersistsEnableAndDisableState()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();
        var package = library.Import(CreateWeaveJar(temp.Path, "state.jar", "state")).Package!;
        package = library.Packages.Single();

        library.SetEnabled(package, true);
        var enabledReload = new LocalPackageLibrary(paths, new JarMetadataParser());
        enabledReload.Load();
        Assert.True(enabledReload.Packages.Single().IsEnabled);

        enabledReload.SetEnabled(enabledReload.Packages.Single(), false);
        var disabledReload = new LocalPackageLibrary(paths, new JarMetadataParser());
        disabledReload.Load();
        Assert.False(disabledReload.Packages.Single().IsEnabled);
    }

    [Fact]
    public void Migration_ImportsLegacyRecoveryAndThirdPartyRecordsOnceWithoutDuplicates()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        CreateWeaveJar(paths.MoonriseOwnedModsDirectory, "recovery.jar", "recovery");
        CreateWeaveJar(paths.UserModsDirectory, "Stormy.jar", "stormy");
        var library = new LocalPackageLibrary(paths, new JarMetadataParser());
        library.Load();

        var migrated = library.LastLayoutMigrationResult!;
        _ = library.MigrateLegacyPackagesOnce([], []);
        var secondPass = library.MigrateLegacyPackagesOnce([], []);

        Assert.Equal(2, migrated.AddFilesMigrated);
        Assert.Equal(0, secondPass);
        Assert.Equal(2, library.Packages.Count);
        Assert.False(library.Packages.Single(item => item.OriginalFileName == "recovery.jar").IsEnabled);
        Assert.False(library.Packages.Single(item => item.OriginalFileName == "Stormy.jar").IsEnabled);
        Assert.All(library.Packages, item => Assert.Equal("untested", item.CompatibilityStatus));
    }

    [Fact]
    public void ManualTypeSelection_RequiresDeveloperModeThenMakesUnclassifiedPackageLaunchable()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var plain = Path.Combine(temp.Path, "advanced.jar");
        CreateJar(plain, null, "Manifest-Version: 1.0\r\n", "advanced");
        library.Import(plain);
        var package = library.Packages.Single();

        Assert.Throws<InvalidOperationException>(() =>
            library.SelectType(package, PackageKind.JavaAgent, developerMode: false));
        library.SelectType(package, PackageKind.JavaAgent, developerMode: true);
        package = library.Packages.Single();
        library.SetEnabled(package, true);

        Assert.Single(new PackageLaunchResolver(new JarMetadataParser())
            .Resolve(library.Packages).JavaAgents);
    }

    [Fact]
    public void Removal_DeletesManagedCopyAndMetadataButPreservesExternalOriginal()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var source = CreateWeaveJar(temp.Path, "external.jar", "external");
        library.Import(source);
        var package = library.Packages.Single();
        var managed = package.FullPath;

        library.Remove(package);

        Assert.Empty(library.Packages);
        Assert.False(File.Exists(managed));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void IntegrityFailure_DisablesPackageWithoutRepairingIt()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        library.Import(CreateWeaveJar(temp.Path, "corrupt.jar", "corrupt"));
        var package = library.Packages.Single();
        library.SetEnabled(package, true);
        File.AppendAllText(package.FullPath, "changed");

        Assert.False(library.VerifyIntegrity(package));
        Assert.False(library.Packages.Single().IsEnabled);
        Assert.Equal("failed", library.Packages.Single().LastIntegrityCheckResult);
    }

    [Fact]
    public void LaunchResolution_ExcludesDisabledAndDeduplicatesEnabledPackages()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        library.Import(CreateWeaveJar(temp.Path, "enabled.jar", "enabled"));
        library.Import(CreateAgentJar(temp.Path, "disabled.jar", "disabled.Agent"));
        var enabled = library.Packages.Single(item => item.Kind == PackageKind.WeaveMod);
        library.SetEnabled(enabled, true);

        var resolver = new PackageLaunchResolver(new JarMetadataParser());
        var selection = resolver.Resolve(library.Packages.Concat([enabled, enabled]));

        Assert.Single(selection.WeaveMods);
        Assert.Empty(selection.JavaAgents);
    }

    [Fact]
    public void LaunchResolution_BlocksEnabledUnclassifiedPackageClearly()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var plain = Path.Combine(temp.Path, "plain.jar");
        CreateJar(plain, null, "Manifest-Version: 1.0\r\n", "plain");
        library.Import(plain);
        var package = library.Packages.Single();
        package.IsEnabled = true;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PackageLaunchResolver(new JarMetadataParser()).Resolve([package]));

        Assert.Contains("Unclassified package cannot be launched", exception.Message);
    }

    [Fact]
    public void EnabledModLaunchView_HandlesSameFilenameWithoutOverwriting()
    {
        using var temp = new TestDirectory();
        var one = Directory.CreateDirectory(Path.Combine(temp.Path, "one")).FullName;
        var two = Directory.CreateDirectory(Path.Combine(temp.Path, "two")).FullName;
        var library = CreateLibrary(temp.Path);
        library.Import(CreateWeaveJar(one, "same.jar", "one"));
        library.Import(CreateWeaveJar(two, "same.jar", "two"));
        foreach (var package in library.Packages)
            library.SetEnabled(package, true);
        var launch = Path.Combine(temp.Path, "launch");
        Directory.CreateDirectory(launch);

        var enabledDirectory = new EnabledModDirectoryService(new JarMetadataParser())
            .Create(launch, library.Packages);

        Assert.Equal(2, Directory.EnumerateFiles(enabledDirectory, "*.jar").Count());
        Assert.All(
            Directory.EnumerateFiles(enabledDirectory, "*.jar"),
            path => Assert.Equal(64, Path.GetFileNameWithoutExtension(path).Length));
    }

    [Fact]
    public void ManagedStorageSize_CountsOneCopyPerUniqueHash()
    {
        using var temp = new TestDirectory();
        var library = CreateLibrary(temp.Path);
        var source = CreateWeaveJar(temp.Path, "size.jar", "size");
        var copy = Path.Combine(temp.Path, "size-copy.jar");
        File.Copy(source, copy);
        library.Import(source);
        library.Import(copy);

        Assert.Equal(new FileInfo(source).Length, library.CalculateManagedStorageSize());
    }

    [Fact]
    public void RuntimeDataLayout_StaysUnderSingleMoonriseRoot()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();

        Assert.Equal(Path.Combine(temp.Path, "packages", "weave"), paths.WeavePackagesDirectory);
        Assert.Equal(Path.Combine(temp.Path, "packages", "agents"), paths.AgentPackagesDirectory);
        Assert.Equal(Path.Combine(temp.Path, "packages", "unclassified"), paths.UnclassifiedPackagesDirectory);
        Assert.Equal(Path.Combine(temp.Path, "packages", "metadata", "index.json"), paths.PackageIndexPath);
        Assert.All(
            new[]
            {
                paths.PackagesDirectory,
                paths.AdaptersDirectory,
                paths.CacheDirectory,
                paths.SettingsDirectory,
                paths.LogsDirectory,
                paths.TempDirectory
            },
            path => Assert.StartsWith(temp.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private static LocalPackageLibrary CreateLibrary(string root)
    {
        var library = new LocalPackageLibrary(new AppPaths(root), new JarMetadataParser());
        library.Load();
        return library;
    }

    private static void Enable(LocalPackageLibrary library, PackageInfo imported)
    {
        var package = library.Packages.Single(item => item.PackageId == imported.PackageId);
        library.SetEnabled(package, true);
    }

    private static string CreateWeaveJar(string directory, string fileName, string id)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        CreateJar(path, ValidWeaveJson(id), null, id);
        return path;
    }

    private static string CreateAgentJar(string directory, string fileName, string entrypoint)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        CreateJar(path, null, Manifest(entrypoint), entrypoint);
        return path;
    }

    private static string ValidWeaveJson(string id) =>
        $$"""{"name":"{{id}}","modId":"{{id}}","version":"1.0.0","entrypoints":["{{id}}.Main"]}""";

    private static string Manifest(string entrypoint) =>
        $"Manifest-Version: 1.0\r\nPremain-Class: {entrypoint}\r\nImplementation-Version: 1.0.0\r\n";

    private static void CreateJar(
        string path,
        string? weaveJson,
        string? manifest,
        string payload)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        if (weaveJson is not null)
        {
            using var writer = new StreamWriter(archive.CreateEntry("weave.mod.json").Open(), new UTF8Encoding(false));
            writer.Write(weaveJson);
        }
        if (manifest is not null)
        {
            using var writer = new StreamWriter(archive.CreateEntry("META-INF/MANIFEST.MF").Open(), new UTF8Encoding(false));
            writer.Write(manifest);
        }
        using var payloadWriter = new StreamWriter(archive.CreateEntry("payload.txt").Open(), new UTF8Encoding(false));
        payloadWriter.Write(payload);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-stage2a-tests-{Guid.NewGuid():N}");
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
