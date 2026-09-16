using System.IO.Compression;
using System.Text;
using Moonrise.Infrastructure;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class WeaveLoaderCompatibilityTests
{
    [Fact]
    public void Inspector_DetectsLegacyCurrentAndMixedApiTargets()
    {
        using var temp = new TemporaryDirectory();
        var parser = new JarMetadataParser();
        var legacy = parser.ParseWeaveMod(CreateMod(temp.Path, "legacy.jar", "net/weavemc/loader/api/ModInitializer"));
        var current = parser.ParseWeaveMod(CreateMod(temp.Path, "current.jar", "net/weavemc/api/ModInitializer"));
        var mixed = parser.ParseWeaveMod(CreateMod(
            temp.Path,
            "mixed.jar",
            "net/weavemc/loader/api/Hook net/weavemc/api/Hook"));

        var inspector = new WeaveModApiInspector();

        Assert.Equal(WeaveApiGeneration.Legacy02, inspector.Inspect(legacy).Generation);
        Assert.Equal(WeaveApiGeneration.Current, inspector.Inspect(current).Generation);
        Assert.Equal(WeaveApiGeneration.Mixed, inspector.Inspect(mixed).Generation);
        Assert.True(inspector.Inspect([legacy]).IsLegacyOnly);
        Assert.True(inspector.Inspect([legacy, current]).HasCurrent);
    }

    [Fact]
    public void LegacyDirectoryAdapter_IsDeterministicAndContainsValidatedPremain()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        var service = new LegacyWeaveDirectoryAdapterService(paths);

        var first = service.Ensure();
        var firstHash = LocalPackageLibrary.ComputeSha256(first);
        var second = service.Ensure();

        Assert.Equal(first, second);
        Assert.Equal(firstHash, LocalPackageLibrary.ComputeSha256(second));
        using var archive = ZipFile.OpenRead(first);
        Assert.NotNull(archive.GetEntry("dev/moonrise/compat/LegacyWeaveDirectoryAgent.class"));
        Assert.NotNull(archive.GetEntry("dev/moonrise/compat/LegacyWeaveDirectoryAgent$LegacyLoaderTransformer.class"));
        using var reader = new StreamReader(archive.GetEntry("META-INF/MANIFEST.MF")!.Open());
        Assert.Contains(
            $"Premain-Class: {LegacyWeaveDirectoryAdapterService.PremainClass}",
            reader.ReadToEnd(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OfficialLoaderDefinitions_AreVersionedSeparately()
    {
        Assert.Equal("1.3.4", WeaveAgentService.Current.Version);
        Assert.Equal("0.2.6", WeaveAgentService.Legacy02.Version);
        Assert.NotEqual(WeaveAgentService.Current.ExpectedSha256, WeaveAgentService.Legacy02.ExpectedSha256);
        Assert.Equal(64, WeaveAgentService.Legacy02.ExpectedSha256.Length);
    }

    private static string CreateMod(string directory, string name, string classContent)
    {
        var path = Path.Combine(directory, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "weave.mod.json", "{\"entrypoints\":[\"sample.Main\"]}");
        Write(archive, "sample/Main.class", classContent);
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
                $"moonrise-weave-compat-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
