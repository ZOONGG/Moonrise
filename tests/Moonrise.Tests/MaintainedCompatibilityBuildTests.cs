using System.IO.Compression;
using System.Text;
using Moonrise.Infrastructure;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class MaintainedCompatibilityBuildTests
{
    [Fact]
    public void ProductionResolution_PreservesOriginalVeyraSelection()
    {
        using var temp = new TemporaryDirectory();
        var original = new Moonrise.Models.PackageInfo
        {
            FileName = "renamed.jar", DisplayName = "arbitrary", Identifier = "arbitrary",
            FullPath = Path.Combine(temp.Path, "renamed.jar"), Kind = Moonrise.Models.PackageKind.WeaveMod,
            Sha256 = "779760392BCA1F269D0EB325E78950249D355E94A09C90988108C3CCD94D95BD"
        };
        var result = new MaintainedCompatibilityBuildService(new AppPaths(temp.Path), new JarMetadataParser())
            .Resolve("1.8.9", [original]);
        Assert.Same(original, Assert.Single(result.LaunchMods));
        Assert.Empty(result.AppliedBuilds);
    }

    [Theory]
    [InlineData("C39F6F1C891825CD309F543D1EE112522CAC3660AC60D9E96A5A21F20756752D", "moonrise-local-bwh-c39f-weave-1.3.4", "6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57.jar")]
    [InlineData("C9C9DC9E75E37291E2992CA7FBD0FF6EB851B811391D9C938DEC14AB73315C05", "moonrise-local-unrestricted-c9c9-weave-1.3.4", "adb858f75da30eace656d380021dca0fd0a047ed6aabdaaa68f758f7965a4833.jar")]
    public void ProductionResolution_KeepsRequiredBuildsUntilRuntimeBridgeIsVerified(string hash, string id, string buildName)
    {
        using var temp = new TemporaryDirectory();
        var original = new Moonrise.Models.PackageInfo
        {
            FileName = "renamed.jar", DisplayName = "arbitrary", Identifier = "arbitrary",
            FullPath = Path.Combine(temp.Path, "renamed.jar"), Kind = Moonrise.Models.PackageKind.WeaveMod,
            Sha256 = hash
        };
        var error = Assert.Throws<FileNotFoundException>(() =>
            new MaintainedCompatibilityBuildService(new AppPaths(temp.Path), new JarMetadataParser())
                .Resolve("1.8.9", [original]));
        Assert.Contains(id, error.Message);
        Assert.Equal(buildName, Path.GetFileName(error.FileName));
        Assert.Throws<InvalidDataException>(() =>
            new MaintainedCompatibilityBuildService(new AppPaths(temp.Path), new JarMetadataParser())
                .Resolve("1.12.2", [original]));
    }

    [Fact]
    public void Resolve_UsesExactLocalBuildWithoutChangingOriginal()
    {
        using var temp = new TemporaryDirectory();
        var parser = new JarMetadataParser();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        var originalPath = CreateWeaveMod(temp.Path, "legacy.jar", "legacy", legacyApi: true);
        var buildSource = CreateWeaveMod(temp.Path, "build.jar", "maintained", legacyApi: false);
        var original = parser.ParseWeaveMod(originalPath);
        var originalBytes = File.ReadAllBytes(originalPath);
        var buildHash = LocalPackageLibrary.ComputeSha256(buildSource);
        var buildName = $"{buildHash.ToLowerInvariant()}.jar";
        var buildDirectory = Path.Combine(paths.AdaptersDirectory, "maintained-builds");
        Directory.CreateDirectory(buildDirectory);
        File.Copy(buildSource, Path.Combine(buildDirectory, buildName));
        var rule = new MaintainedCompatibilityBuildRule(
            "test-adapter",
            original.Sha256,
            buildHash,
            buildName,
            "1.8.9",
            WeaveAgentService.Version);

        var result = new MaintainedCompatibilityBuildService(paths, parser, [rule])
            .Resolve("1.8.9", [original]);

        var applied = Assert.Single(result.AppliedBuilds);
        Assert.Equal("test-adapter", applied.Rule.Id);
        Assert.Equal(buildHash, Assert.Single(result.LaunchMods).Sha256);
        Assert.Equal(originalBytes, File.ReadAllBytes(originalPath));
    }

    [Fact]
    public void Resolve_UsesImportedExactBuildRegardlessOfFilenameAndEnabledState()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        var parser = new JarMetadataParser();
        var original = parser.ParseWeaveMod(CreateWeaveMod(temp.Path, "legacy.jar", "legacy", true));
        var imported = parser.ParseWeaveMod(CreateWeaveMod(paths.WeavePackagesDirectory, "user-renamed.jar", "maintained", false));
        var rule = new MaintainedCompatibilityBuildRule("fixture", original.Sha256, imported.Sha256,
            "absent-canonical.jar", "1.8.9", WeaveAgentService.Version);
        var before = File.ReadAllBytes(original.FullPath);
        var result = new MaintainedCompatibilityBuildService(paths, parser, [rule]).Resolve("1.8.9", [original]);
        Assert.Equal(imported.FullPath, Assert.Single(result.LaunchMods).FullPath);
        Assert.Single(result.AppliedBuilds);
        Assert.Equal(before, File.ReadAllBytes(original.FullPath));
    }

    [Fact]
    public void Resolve_RejectsCompatibilityBuildWithUnexpectedHash()
    {
        using var temp = new TemporaryDirectory();
        var parser = new JarMetadataParser();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        var original = parser.ParseWeaveMod(
            CreateWeaveMod(temp.Path, "legacy.jar", "legacy", legacyApi: true));
        var buildDirectory = Path.Combine(paths.AdaptersDirectory, "maintained-builds");
        Directory.CreateDirectory(buildDirectory);
        var buildName = "unexpected.jar";
        CreateWeaveMod(buildDirectory, buildName, "unexpected", legacyApi: false);
        var rule = new MaintainedCompatibilityBuildRule(
            "test-adapter",
            original.Sha256,
            new string('A', 64),
            buildName,
            "1.8.9",
            WeaveAgentService.Version);

        var exception = Assert.Throws<InvalidDataException>(() =>
            new MaintainedCompatibilityBuildService(paths, parser, [rule])
                .Resolve("1.8.9", [original]));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentResolutionPreservesOriginalAndUsesValidatedAgentBuild()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path); paths.EnsureUserDirectories();
        var parser = new JarMetadataParser();
        var original = parser.ParseJavaAgent(CreateAgent(temp.Path, "original.jar", "original.Agent"));
        var build = parser.ParseJavaAgent(CreateAgent(paths.AgentPackagesDirectory, "renamed.jar", "fixed.Agent"));
        var rule = new MaintainedCompatibilityBuildRule("agent-fixture", original.Sha256, build.Sha256,
            "missing-name.jar", "1.8.9", WeaveAgentService.Version);
        var before = File.ReadAllBytes(original.FullPath);
        var result = new MaintainedCompatibilityBuildService(paths, parser, [], _ => {}, [rule])
            .ResolveAgents("1.8.9", [original]);
        var selected = Assert.Single(result.LaunchMods);
        Assert.Equal(Moonrise.Models.PackageKind.JavaAgent, selected.Kind);
        Assert.Equal(build.Sha256, selected.Sha256);
        Assert.Equal(before, File.ReadAllBytes(original.FullPath));
        Assert.Single(result.AppliedBuilds);
    }

    [Fact]
    public void AgentResolutionRejectsChangedMaintainedArtifact()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path); paths.EnsureUserDirectories();
        var parser = new JarMetadataParser();
        var original = parser.ParseJavaAgent(CreateAgent(temp.Path, "original.jar", "original.Agent"));
        var directory = Path.Combine(paths.AdaptersDirectory, "maintained-builds"); Directory.CreateDirectory(directory);
        var artifact = CreateAgent(directory, "fixed.jar", "fixed.Agent");
        var hash = LocalPackageLibrary.ComputeSha256(artifact);
        var rule = new MaintainedCompatibilityBuildRule("agent-fixture", original.Sha256, hash,
            "fixed.jar", "1.8.9", WeaveAgentService.Version);
        using (var jar = ZipFile.Open(artifact, ZipArchiveMode.Update)) Write(jar, "changed.txt", "changed");
        Assert.Throws<InvalidDataException>(() => new MaintainedCompatibilityBuildService(paths, parser, [], _ => {}, [rule])
            .ResolveAgents("1.8.9", [original]));
    }

    private static string CreateAgent(string directory, string name, string entrypoint)
    {
        var path = Path.Combine(directory, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "META-INF/MANIFEST.MF", "Manifest-Version: 1.0\r\nPremain-Class: " + entrypoint + "\r\n\r\n");
        return path;
    }

    private static string CreateWeaveMod(
        string directory,
        string fileName,
        string id,
        bool legacyApi)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "weave.mod.json",
            $$"""{"name":"{{id}}","modId":"{{id}}","namespace":"official","entryPoints":["sample.Main"]}""");
        Write(
            archive,
            "sample/Main.class",
            legacyApi ? "net/weavemc/loader/api/ModInitializer" : "net/weavemc/api/ModInitializer");
        return path;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-maintained-build-tests-{Guid.NewGuid():N}");
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
