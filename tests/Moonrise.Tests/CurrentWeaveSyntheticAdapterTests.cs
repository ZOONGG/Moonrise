using System.IO.Compression;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;
public sealed class CurrentWeaveSyntheticAdapterTests
{
    [Fact]
    public void UnknownGenesisRemainsRejectedAndUnchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), "moonrise-genesis-" + Guid.NewGuid().ToString("N") + ".jar");
        try
        {
            File.WriteAllText(path, "unverified Genesis");
            Assert.Throws<InvalidDataException>(() => CurrentWeaveSyntheticAdapter.ValidateGenesisHash(path));
            Assert.Equal("unverified Genesis", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(WeaveRuntimeMode.Disabled)]
    [InlineData(WeaveRuntimeMode.Legacy)]
    [InlineData(WeaveRuntimeMode.Custom)]
    public void OtherModesStayIdentical(WeaveRuntimeMode mode)
    {
        var plan = new LaunchPlan(mode, [], [], [], []);
        Assert.Same(plan, CurrentWeaveSyntheticAdapter.Prepare(plan, "unused"));
    }
    [Fact]
    public void UnknownLoaderFailsWithoutWritingOrChangingOriginal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "moonrise-synthetic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var loader = Path.Combine(directory, "loader.jar"); File.WriteAllText(loader, "original");
            var plan = new LaunchPlan(WeaveRuntimeMode.Current, ["mod.jar"],
                [new LaunchAgent(loader, null, "loader", LaunchAgentRole.WeaveLoader)], [], []);
            Assert.Throws<InvalidDataException>(() => CurrentWeaveSyntheticAdapter.Prepare(plan, directory));
            Assert.Equal("original", File.ReadAllText(loader)); Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void EmbeddedAdapterIsSeparateJava17AgentWithoutFixtureClasses()
    {
        var path = Path.Combine(Path.GetTempPath(), "moonrise-synthetic-" + Guid.NewGuid().ToString("N") + ".jar");
        try
        {
            CurrentWeaveSyntheticAdapter.WriteJar(path);
            using var jar = ZipFile.OpenRead(path);
            Assert.Equal(5, jar.Entries.Count);
            using var manifest = new StreamReader(jar.GetEntry("META-INF/MANIFEST.MF")!.Open());
            Assert.Contains("Premain-Class: dev.moonrise.current.CurrentSyntheticAgent", manifest.ReadToEnd());
            foreach (var entry in jar.Entries.Where(e => e.FullName.EndsWith(".class")))
            {
                Assert.DoesNotContain("Test", entry.FullName);
                using var input = entry.Open(); var header = new byte[8]; input.ReadExactly(header);
                Assert.Equal(new byte[]{0xca,0xfe,0xba,0xbe,0,0,0,61}, header);
            }
        }
        finally { File.Delete(path); }
    }
}
