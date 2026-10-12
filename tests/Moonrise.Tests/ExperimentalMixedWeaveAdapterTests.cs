using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class ExperimentalMixedWeaveAdapterTests
{
    [Fact]
    public void EmptyOrMissingSelectionsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => ExperimentalMixedWeaveAdapter.ValidateSelection([], []));
        var package = new PackageInfo { FileName="unknown.jar", DisplayName="unknown", Identifier="unknown",
            FullPath="unknown.jar", Kind=PackageKind.WeaveMod, Sha256="unknown" };
        Assert.Throws<InvalidDataException>(() => ExperimentalMixedWeaveAdapter.ValidateSelection([package, package], []));
    }

    [Theory]
    [InlineData(WeaveRuntimeMode.Legacy)]
    [InlineData(WeaveRuntimeMode.Disabled)]
    [InlineData(WeaveRuntimeMode.Custom)]
    public void CompetingRuntimeCannotEnterExperiment(WeaveRuntimeMode mode)
    {
        var plan = new LaunchPlan(mode, [], [], [], []);
        Assert.Throws<InvalidDataException>(() => ExperimentalMixedWeaveAdapter.Prepare(plan, "unused", "unused"));
    }

    [Fact]
    public void UnverifiedLoaderFailsBeforeAdapterStaging()
    {
        var directory = Path.Combine(Path.GetTempPath(), "moonrise-mixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "loader.jar"); File.WriteAllText(path, "original");
            var plan = new LaunchPlan(WeaveRuntimeMode.Current, [],
                [new LaunchAgent(path, null, "loader", LaunchAgentRole.WeaveLoader)], [], []);
            Assert.Throws<InvalidDataException>(() => ExperimentalMixedWeaveAdapter.Prepare(plan, directory, "unused"));
            Assert.Single(Directory.GetFiles(directory)); Assert.Equal("original", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
