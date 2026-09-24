using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class CurrentLaunchPlanFactoryTests
{
    [Fact]
    public void CurrentWeavePlanMatchesExistingLoaderThenAgentOrder()
    {
        var plan = new CurrentLaunchPlanFactory().Create(
            [Package("mod", PackageKind.WeaveMod, @"C:\packages\mod.jar")],
            [Package("agent", PackageKind.JavaAgent, @"C:\packages\agent.jar")],
            @"C:\runtime\weave-current.jar",
            useLegacyWeave: false);

        Assert.Equal(WeaveRuntimeMode.Current, plan.WeaveMode);
        Assert.Equal([@"C:\packages\mod.jar"], plan.ModPaths);
        Assert.Equal(
            ["weave-loader-current", "agent"],
            plan.Agents.Select(item => item.RuntimeId).ToArray());
    }

    [Fact]
    public void LegacyBwhPlanMatchesExistingNetworkAdapterLegacyAdapterLoaderAgentOrder()
    {
        var plan = new CurrentLaunchPlanFactory().Create(
            [Package("bwh", PackageKind.WeaveMod, @"C:\packages\bwh.jar")],
            [Package("agent", PackageKind.JavaAgent, @"C:\packages\agent.jar")],
            @"C:\runtime\weave-legacy.jar",
            useLegacyWeave: true,
            legacyWeaveAdapterPath: @"C:\runtime\legacy-directory.jar",
            networkAdapterPath: @"C:\runtime\bwh-network.jar");

        Assert.Equal(WeaveRuntimeMode.Legacy, plan.WeaveMode);
        Assert.Equal(
            [
                "moonrise-bwh-network-adapter",
                LegacyWeaveDirectoryAdapterService.Id,
                "weave-loader-legacy",
                "agent"
            ],
            plan.Agents.Select(item => item.RuntimeId).ToArray());

        var projection = Mnr3BridgeProjectionBuilder.Build(plan);
        Assert.Equal(@"C:\runtime\bwh-network.jar", projection.PrimaryAgentPath);
        Assert.Equal(
            [
                @"C:\runtime\legacy-directory.jar",
                @"C:\runtime\weave-legacy.jar",
                @"C:\packages\agent.jar"
            ],
            projection.AdditionalAgentPaths);
    }

    [Fact]
    public void JavaAgentOnlyPlanDisablesWeaveWithoutInventingLoader()
    {
        var plan = new CurrentLaunchPlanFactory().Create(
            [],
            [
                Package("agent-a", PackageKind.JavaAgent, @"C:\packages\a.jar"),
                Package("agent-b", PackageKind.JavaAgent, @"C:\packages\b.jar")
            ],
            weaveLoaderPath: null,
            useLegacyWeave: false);

        Assert.Equal(WeaveRuntimeMode.Disabled, plan.WeaveMode);
        Assert.Empty(plan.ModPaths);
        Assert.Equal(
            ["agent-a", "agent-b"],
            plan.Agents.Select(item => item.RuntimeId).ToArray());
    }

    [Fact]
    public void WeaveModsRequireLoaderButAgentOnlyLaunchDoesNot()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CurrentLaunchPlanFactory().Create(
                [Package("mod", PackageKind.WeaveMod, @"C:\packages\mod.jar")],
                [],
                weaveLoaderPath: null,
                useLegacyWeave: false));

        Assert.Contains("loader path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyModsRequireTheirCompatibilityAdapter()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CurrentLaunchPlanFactory().Create(
                [Package("legacy-mod", PackageKind.WeaveMod, @"C:\packages\legacy.jar")],
                [],
                @"C:\runtime\weave-legacy.jar",
                useLegacyWeave: true));

        Assert.Contains("compatibility adapter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentWeaveRejectsLegacyAdapter()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CurrentLaunchPlanFactory().Create(
                [Package("mod", PackageKind.WeaveMod, @"C:\packages\mod.jar")],
                [],
                @"C:\runtime\weave-current.jar",
                useLegacyWeave: false,
                legacyWeaveAdapterPath: @"C:\runtime\legacy.jar"));

        Assert.Contains("legacy Weave adapter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PackageInfo Package(string id, PackageKind kind, string path) => new()
    {
        PackageId = id,
        FileName = Path.GetFileName(path),
        OriginalFileName = Path.GetFileName(path),
        DisplayName = id,
        Identifier = id,
        FullPath = path,
        Kind = kind,
        Version = "1.0",
        Entrypoint = "test",
        Size = 1,
        Sha256 = new string('A', 64),
        IsEnabled = true
    };
}
