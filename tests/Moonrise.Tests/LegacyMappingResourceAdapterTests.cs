using Moonrise.Models;
using Moonrise.Services;
using Xunit;
namespace Moonrise.Tests;
public sealed class LegacyMappingResourceAdapterTests
{
    [Fact] public void CleanLaunchIsUnchanged()
    {
        var plan = new LaunchPlan(WeaveRuntimeMode.Disabled, [], [], [], []);
        Assert.Same(plan, LegacyMappingResourceAdapter.Prepare(plan, "unused"));
    }
    [Fact] public void UnsupportedLoaderFailsBeforeWritingSessionArtifacts()
    {
        using var temp = new TestDirectory();
        var loader = Path.Combine(temp.Path, "loader.jar");
        File.WriteAllText(loader, "unsupported");
        var plan = new LaunchPlan(WeaveRuntimeMode.Legacy, [],
            [new LaunchAgent(loader, null, "weave-loader-legacy", LaunchAgentRole.WeaveLoader)], [], []);
        Assert.Throws<InvalidDataException>(() => LegacyMappingResourceAdapter.Prepare(plan, temp.Path));
        Assert.Single(Directory.GetFiles(temp.Path));
        Assert.Equal("unsupported", File.ReadAllText(loader));
    }
    [Fact] public void MissingMappingFailsClosed() => Assert.Throws<InvalidDataException>(() =>
        LegacyMappingResourceAdapter.ValidateHash(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".jar"), "missing"));
    private sealed class TestDirectory : IDisposable { public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString()); public TestDirectory() { Directory.CreateDirectory(Path); } public void Dispose() { Directory.Delete(Path, true); } }
    [Fact] public void BootstrapJarHasStandaloneJava17EntrypointAndDiagnosticDependency()
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Path, "bootstrap.jar");
        LegacyMappingResourceAdapter.WriteBootstrapJar(path);
        using var jar = System.IO.Compression.ZipFile.OpenRead(path);
        using var manifest = new StreamReader(jar.GetEntry("META-INF/MANIFEST.MF")!.Open());
        Assert.Equal("Manifest-Version: 1.0\r\nPremain-Class: dev.moonrise.mapping.MappingAgent\r\n\r\n", manifest.ReadToEnd());
        Assert.Equal(4, jar.Entries.Count);
        foreach (var name in new[] { "MappingAgent", "LegacyMappingAdapter", "StartupTrace" })
        {
            using var input = jar.GetEntry($"dev/moonrise/mapping/{name}.class")!.Open();
            var header = new byte[8]; input.ReadExactly(header);
            Assert.Equal(new byte[] { 0xca, 0xfe, 0xba, 0xbe, 0, 0, 0, 61 }, header);
        }
    }
    [Fact] public void ExactResourcePath() => Assert.Equal("lunar/lunar_named_b5_1.8.9.xsrg", LegacyMappingResourceAdapter.ResourcePath);
}

