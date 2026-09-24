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
                new LaunchAgent(@"C:\runtime\weave.jar", null, null, true),
                new LaunchAgent(@"C:\packages\agent.jar", "mode=strict", "agent-a", false)
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
            "agent\tC:\\runtime\\weave.jar\t\t\t1\n" +
            "agent\tC:\\packages\\agent.jar\tmode=strict\tagent-a\t0\n" +
            "arg\t-Xmx4G\n" +
            "arg\t-XX:+UseG1GC\n" +
            "prop\tmoonrise.test\tone\n" +
            "prop\texample.mode\ttwo\n",
            config);
    }

    [Fact]
    public void LaunchPlanBuilder_RejectsTabsAndLineBreaksInFields()
    {
        var plan = new LaunchPlan(
            WeaveRuntimeMode.Disabled,
            [],
            [new LaunchAgent(@"C:\packages\agent.jar", "bad\toption", "agent-a", false)],
            [],
            []);

        var exception = Assert.Throws<ArgumentException>(() =>
            NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, @"C:\runtime\mods"));

        Assert.Contains("unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
