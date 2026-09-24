using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class NativeBridgeConfigurationTests
{
    [Fact]
    public void LegacyBuilder_RemainsMnr3()
    {
        var config = NativeBridgeConfigurationBuilder.Build(
            @"C:\runtime\primary.jar",
            @"C:\runtime\mods",
            [@"C:\runtime\second.jar"]);

        Assert.Equal(
            "MNR3\n" +
            "C:\\runtime\\primary.jar\n" +
            "C:\\runtime\\mods\n" +
            "C:\\runtime\\second.jar\n",
            config);
    }

    [Fact]
    public void LaunchPlanBuilder_SerializesOrderedAgentsOptionsArgsAndProperties()
    {
        var plan = new LaunchPlan(
            WeaveRuntimeMode.Current,
            [@"C:\packages\mod-a.jar"],
            [
                new LaunchAgent(@"C:\runtime\weave.jar", null, "weave-loader-current", LaunchAgentRole.WeaveLoader),
                new LaunchAgent(@"C:\packages\agent.jar", "mode=strict", "agent-a", LaunchAgentRole.PackageAgent)
            ],
            ["-Xmx4G", "-XX:+UseG1GC"],
            [
                new LaunchJvmProperty("moonrise.test", "one"),
                new LaunchJvmProperty("example.mode", "two")
            ]);

        var config = NativeBridgeConfigurationBuilder.BuildLaunchPlan(
            plan,
            @"C:\runtime\mods");

        Assert.Equal(
            "MNR4\n" +
            "mode\tCurrent\n" +
            "mods\tC:\\runtime\\mods\n" +
            "agent\tC:\\runtime\\weave.jar\t\tweave-loader-current\tWeaveLoader\n" +
            "agent\tC:\\packages\\agent.jar\tmode=strict\tagent-a\tPackageAgent\n" +
            "arg\t-Xmx4G\n" +
            "arg\t-XX:+UseG1GC\n" +
            "prop\tmoonrise.test\tone\n" +
            "prop\texample.mode\ttwo\n",
            config);
    }

    [Fact]
    public void LaunchPlanWriter_WritesMnr4Atomically()
    {
        var root = Path.Combine(Path.GetTempPath(), $"moonrise-bridge-config-{Guid.NewGuid():N}");
        try
        {
            var output = Path.Combine(root, "session", "bridge.cfg");
            var plan = new LaunchPlan(
                WeaveRuntimeMode.Disabled,
                [],
                [new LaunchAgent(@"C:\packages\agent.jar", "mode=strict", "agent-a", LaunchAgentRole.PackageAgent)],
                ["-Xmx2G"],
                [new LaunchJvmProperty("moonrise.test", "1")]);

            BridgeConfigurationFile.WriteAtomic(output, plan, @"C:\runtime\mods");

            var text = File.ReadAllText(output);
            Assert.StartsWith("MNR4\nmode\tDisabled\n", text, StringComparison.Ordinal);
            Assert.Contains("agent\tC:\\packages\\agent.jar\tmode=strict\tagent-a\tPackageAgent\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".tmp", string.Join("|", Directory.EnumerateFiles(Path.GetDirectoryName(output)!, "*", SearchOption.TopDirectoryOnly)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LaunchPlanBuilder_RejectsTabInRuntimePath()
    {
        var plan = new LaunchPlan(
            WeaveRuntimeMode.Disabled,
            [],
            [new LaunchAgent("C:\\packages\\bad\tagent.jar", null, "agent-a", LaunchAgentRole.PackageAgent)],
            [],
            []);

        Assert.Throws<ArgumentException>(() =>
            NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, @"C:\runtime\mods"));
    }

    [Fact]
    public void LaunchPlanBuilder_RejectsTabsAndLineBreaksInFields()
    {
        var plan = new LaunchPlan(
            WeaveRuntimeMode.Disabled,
            [],
            [new LaunchAgent(@"C:\packages\agent.jar", "bad\toption", "agent-a", LaunchAgentRole.PackageAgent)],
            [],
            []);

        var exception = Assert.Throws<ArgumentException>(() =>
            NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, @"C:\runtime\mods"));

        Assert.Contains("unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
