using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class Mnr3BridgeProjectionTests
{
    [Fact]
    public void CurrentWeaveProjectionMatchesExistingAgentOrder()
    {
        var plan = new LaunchPlanBuilder().Build(
            [Agent("agent-a", @"C:\packages\agent-a.jar")],
            WeaveRuntimeMode.Current,
            @"C:\runtime\weave-current.jar");

        var projection = Mnr3BridgeProjectionBuilder.Build(plan);

        Assert.Equal(@"C:\runtime\weave-current.jar", projection.PrimaryAgentPath);
        Assert.Equal(
            [@"C:\packages\agent-a.jar"],
            projection.AdditionalAgentPaths);
    }

    [Fact]
    public void LegacyAndNetworkAdaptersProjectToExistingPrimaryAdditionalOrder()
    {
        var plan = new LaunchPlanBuilder().Build(
            [Agent("package-agent", @"C:\packages\package-agent.jar")],
            WeaveRuntimeMode.Legacy,
            @"C:\runtime\weave-legacy.jar",
            technicalAgents:
            [
                new TechnicalLaunchAgentDescriptor(
                    "bwh-network",
                    @"C:\runtime\bwh-network.jar",
                    LaunchAgentRole.NetworkAdapter),
                new TechnicalLaunchAgentDescriptor(
                    "legacy-directory",
                    @"C:\runtime\legacy-directory.jar",
                    LaunchAgentRole.CompatibilityAdapter)
            ]);

        var projection = Mnr3BridgeProjectionBuilder.Build(plan);

        Assert.Equal(@"C:\runtime\bwh-network.jar", projection.PrimaryAgentPath);
        Assert.Equal(
            [
                @"C:\runtime\legacy-directory.jar",
                @"C:\runtime\weave-legacy.jar",
                @"C:\packages\package-agent.jar"
            ],
            projection.AdditionalAgentPaths);
    }

    [Fact]
    public void JavaAgentOnlyProjectionPreservesPackageOrder()
    {
        var plan = new LaunchPlanBuilder().Build(
            [
                Agent("agent-a", @"C:\packages\a.jar"),
                Agent("agent-b", @"C:\packages\b.jar")
            ],
            WeaveRuntimeMode.Disabled);

        var projection = Mnr3BridgeProjectionBuilder.Build(plan);

        Assert.Equal(@"C:\packages\a.jar", projection.PrimaryAgentPath);
        Assert.Equal([@"C:\packages\b.jar"], projection.AdditionalAgentPaths);
    }

    [Fact]
    public void EmptyPlanDoesNotInventAnAgent()
    {
        var plan = new LaunchPlanBuilder().Build([], WeaveRuntimeMode.Disabled);

        Assert.Throws<InvalidOperationException>(() =>
            Mnr3BridgeProjectionBuilder.Build(plan));
    }

    [Fact]
    public void Mnr3RejectsAgentOptionsInsteadOfSilentlyDroppingThem()
    {
        var plan = new LaunchPlanBuilder().Build(
            [new PackageRuntimeDescriptor(
                "agent-a",
                PackageKind.JavaAgent,
                @"C:\packages\a.jar",
                "mode=strict")],
            WeaveRuntimeMode.Disabled);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Mnr3BridgeProjectionBuilder.Build(plan));

        Assert.Contains("options", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mnr3RejectsExplicitJvmConfigurationInsteadOfSilentlyDroppingIt()
    {
        var withArgument = new LaunchPlanBuilder().Build(
            [Agent("agent-a", @"C:\packages\a.jar")],
            WeaveRuntimeMode.Disabled,
            jvmArguments: ["-Xmx4G"]);
        var withProperty = new LaunchPlanBuilder().Build(
            [Agent("agent-a", @"C:\packages\a.jar")],
            WeaveRuntimeMode.Disabled,
            jvmProperties: [new LaunchJvmProperty("moonrise.test", "1")]);

        Assert.Throws<InvalidOperationException>(() =>
            Mnr3BridgeProjectionBuilder.Build(withArgument));
        Assert.Throws<InvalidOperationException>(() =>
            Mnr3BridgeProjectionBuilder.Build(withProperty));
    }

    [Fact]
    public void Mnr4NativeBridgeLaunchPlanOverloadAcceptsAgentOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"moonrise-mnr4-launch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var agent = Path.Combine(root, "agent.jar");
            File.WriteAllBytes(agent, [0x50, 0x4b, 0x03, 0x04]);
            var plan = new LaunchPlanBuilder().Build(
                [new PackageRuntimeDescriptor(
                    "agent-a",
                    PackageKind.JavaAgent,
                    agent,
                    "mode=strict")],
                WeaveRuntimeMode.Disabled);
            var missingLauncher = Path.Combine(root, "Lunar Client.exe");

            var exception = Assert.Throws<FileNotFoundException>(() =>
                new NativeBridgeLauncher().Launch(
                    missingLauncher,
                    plan,
                    Path.Combine(root, "mods"),
                    Path.Combine(root, "Moonrise.Native.dll"),
                    Path.Combine(root, "bridge-config.txt")));

            Assert.Equal(missingLauncher, exception.FileName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PackageRuntimeDescriptor Agent(string id, string path) =>
        new(id, PackageKind.JavaAgent, path);
}
