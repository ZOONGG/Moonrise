using System.IO.Compression;
using System.Text;
using Moonrise.Infrastructure;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class MaintainedCompatibilityBuildTests
{
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
    public void ResolveMoonriseOwnedSuccessors_ReplacesOnlyExactTargetAndPreservesOriginal()
    {
        using var temp = new TemporaryDirectory();
        var parser = new JarMetadataParser();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        var originalPath = CreateWeaveMod(temp.Path, "stormy.jar", "stormy", legacyApi: true);
        var successorSource = CreateWeaveMod(temp.Path, "veyra.jar", "veyra", legacyApi: false);
        var original = parser.ParseWeaveMod(originalPath);
        var originalBytes = File.ReadAllBytes(originalPath);
        var successorHash = LocalPackageLibrary.ComputeSha256(successorSource);
        var successorName = $"{successorHash.ToLowerInvariant()}.jar";
        var buildDirectory = Path.Combine(paths.AdaptersDirectory, "maintained-builds");
        Directory.CreateDirectory(buildDirectory);
        File.Copy(successorSource, Path.Combine(buildDirectory, successorName));
        var rule = new MoonriseOwnedSuccessorRule(
            "test-successor",
            original.Sha256,
            successorHash,
            successorName,
            "veyra",
            "1.8.9",
            WeaveAgentService.Version);

        var result = new MaintainedCompatibilityBuildService(
                paths,
                parser,
                rules: [],
                successorRules: [rule])
            .ResolveMoonriseOwnedSuccessors("1.8.9", [original]);

        var applied = Assert.Single(result.AppliedSuccessors);
        Assert.Equal("test-successor", applied.Rule.Id);
        Assert.Equal("veyra", Assert.Single(result.LaunchMods).Identifier);
        Assert.Equal(successorHash, applied.SuccessorPackage.Sha256);
        Assert.Equal(originalBytes, File.ReadAllBytes(originalPath));
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
