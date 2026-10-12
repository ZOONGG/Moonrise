using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class CurrentLaunchPlanFactoryTests
{
    [Fact]
    public void LegacySmokeDiagnosticOptionsReachStructuredMnr4WithoutPackageAgents()
    {
        const string options = @"C:\runtime\weave.jar|C:\logs\discovery.log";
        var plan = new CurrentLaunchPlanFactory().CreateLegacyLoaderSmoke([], [],
            @"C:\runtime\weave.jar", @"C:\runtime\diagnostic.jar", options);
        Assert.Empty(plan.ModPaths);
        Assert.Equal(["moonrise-mixin-discovery-diagnostic", "weave-loader-legacy"],
            plan.Agents.Select(agent => agent.RuntimeId).ToArray());
        Assert.DoesNotContain(plan.Agents, agent => agent.Role == LaunchAgentRole.PackageAgent);
        var config = NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, @"C:\runtime\empty-mods");
        Assert.Contains("\t" + options + "\tmoonrise-mixin-discovery-diagnostic\t", config);
    }

    [Fact]
    public void LegacyLoaderSmokeEmitsExactlyOneStructuredAgentAndNoMods()
    {
        var plan = new CurrentLaunchPlanFactory().CreateLegacyLoaderSmoke([], [], @"C:\runtime\weave-0.2.6.jar");
        Assert.Equal(WeaveRuntimeMode.Legacy, plan.WeaveMode);
        Assert.Empty(plan.ModPaths);
        var agent = Assert.Single(plan.Agents);
        Assert.Equal(LaunchAgentRole.WeaveLoader, agent.Role);
        Assert.Equal("weave-loader-legacy", agent.RuntimeId);
        var config = NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, @"C:\runtime\empty-mods");
        Assert.Single(config.Split('\n'), line => line.StartsWith("agent\t", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PackageKind.WeaveMod)]
    [InlineData(PackageKind.JavaAgent)]
    public void LegacyLoaderSmokeRejectsEnabledPackages(PackageKind kind)
    {
        var package = Package("unexpected", kind, @"C:\packages\unexpected.jar");
        Assert.Throws<InvalidOperationException>(() => new CurrentLaunchPlanFactory().CreateLegacyLoaderSmoke(
            kind == PackageKind.WeaveMod ? [package] : [],
            kind == PackageKind.JavaAgent ? [package] : [],
            @"C:\runtime\weave.jar"));
    }

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
    public void CurrentLaunchPlanProjectsToByteEquivalentLegacyMnr3Configuration()
    {
        var plan = new CurrentLaunchPlanFactory().Create(
            [Package("mod", PackageKind.WeaveMod, @"C:\packages\mod.jar")],
            [
                Package("agent-a", PackageKind.JavaAgent, @"C:\packages\a.jar"),
                Package("agent-b", PackageKind.JavaAgent, @"C:\packages\b.jar")
            ],
            @"C:\runtime\weave-current.jar",
            useLegacyWeave: false);

        var projection = Mnr3BridgeProjectionBuilder.Build(plan);
        var fromPlan = NativeBridgeConfigurationBuilder.Build(
            projection.PrimaryAgentPath,
            @"C:\runtime\mods",
            projection.AdditionalAgentPaths);
        var legacy = NativeBridgeConfigurationBuilder.Build(
            @"C:\runtime\weave-current.jar",
            @"C:\runtime\mods",
            [@"C:\packages\a.jar", @"C:\packages\b.jar"]);

        Assert.Equal(legacy, fromPlan);
    }

    [Fact]
    public void CurrentBwhPlanMatchesExistingNetworkAdapterLoaderAgentOrder()
    {
        var plan = new CurrentLaunchPlanFactory().Create(
            [Package("bwh", PackageKind.WeaveMod, @"C:\packages\bwh.jar")],
            [Package("agent", PackageKind.JavaAgent, @"C:\packages\agent.jar")],
            @"C:\runtime\weave-current.jar",
            useLegacyWeave: false,
            networkAdapterPath: @"C:\runtime\bwh-network.jar");

        Assert.Equal(
            ["moonrise-bwh-network-adapter", "weave-loader-current", "agent"],
            plan.Agents.Select(item => item.RuntimeId).ToArray());

        var projection = Mnr3BridgeProjectionBuilder.Build(plan);
        Assert.Equal(@"C:\runtime\bwh-network.jar", projection.PrimaryAgentPath);
        Assert.Equal(
            [@"C:\runtime\weave-current.jar", @"C:\packages\agent.jar"],
            projection.AdditionalAgentPaths);
    }

    [Fact]
    public void LegacyWeavePlanMatchesExistingDirectoryAdapterLoaderAgentOrder()
    {
        var plan = new CurrentLaunchPlanFactory().Create(
            [Package("legacy-mod", PackageKind.WeaveMod, @"C:\packages\legacy.jar")],
            [Package("agent", PackageKind.JavaAgent, @"C:\packages\agent.jar")],
            @"C:\runtime\weave-legacy.jar",
            useLegacyWeave: true,
            legacyWeaveAdapterPath: @"C:\runtime\legacy-directory.jar");

        Assert.Equal(WeaveRuntimeMode.Legacy, plan.WeaveMode);
        Assert.Equal(
            [
                LegacyWeaveDirectoryAdapterService.Id,
                "weave-loader-legacy",
                "agent"
            ],
            plan.Agents.Select(item => item.RuntimeId).ToArray());

        var projection = Mnr3BridgeProjectionBuilder.Build(plan);
        Assert.Equal(@"C:\runtime\legacy-directory.jar", projection.PrimaryAgentPath);
        Assert.Equal(
            [@"C:\runtime\weave-legacy.jar", @"C:\packages\agent.jar"],
            projection.AdditionalAgentPaths);
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
