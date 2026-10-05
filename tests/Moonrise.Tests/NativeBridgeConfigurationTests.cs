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
                new LaunchAgent(@"C:\packages\agent.jar", "mode=strict", "agent-a", LaunchAgentRole.PackageAgent),
                new LaunchAgent(@"C:\packages\agent-b.jar", null, "agent-b", LaunchAgentRole.PackageAgent)
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
            "agent\tC:\\packages\\agent-b.jar\t\tagent-b\tPackageAgent\n" +
            "arg\t-Xmx4G\n" +
            "arg\t-XX:+UseG1GC\n" +
            "prop\tmoonrise.test\tone\n" +
            "prop\texample.mode\ttwo\n",
            config);
    }

    [Fact]
    public void DisabledWeaveConfigurationContainsOnlyPackageAgentsAndExplicitJvmValues()
    {
        var plan = new LaunchPlanBuilder().Build(
            [
                new PackageRuntimeDescriptor(
                    "agent-a",
                    PackageKind.JavaAgent,
                    @"C:\packages\agent-a.jar",
                    "mode=strict"),
                new PackageRuntimeDescriptor(
                    "agent-b",
                    PackageKind.JavaAgent,
                    @"C:\packages\agent-b.jar")
            ],
            WeaveRuntimeMode.Disabled,
            jvmArguments: ["-Xmx4G"],
            jvmProperties: [new LaunchJvmProperty("moonrise.test", "one")]);

        var config = NativeBridgeConfigurationBuilder.BuildLaunchPlan(
            plan,
            @"C:\runtime\mods");

        Assert.Equal(
            "MNR4\n" +
            "mode\tDisabled\n" +
            "mods\tC:\\runtime\\mods\n" +
            "agent\tC:\\packages\\agent-a.jar\tmode=strict\tagent-a\tPackageAgent\n" +
            "agent\tC:\\packages\\agent-b.jar\t\tagent-b\tPackageAgent\n" +
            "arg\t-Xmx4G\n" +
            "prop\tmoonrise.test\tone\n",
            config);
        Assert.DoesNotContain("WeaveLoader", config, StringComparison.Ordinal);
        Assert.DoesNotContain("weave.mods.directory", config, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("weave.api.minecraft.enabled", config, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("weave.dump.bytecode.enabled", config, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchPlanSerializationDoesNotMutatePackageFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"moonrise-package-integrity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "third-party-agent.jar");
            File.WriteAllBytes(package, [0x50, 0x4b, 0x03, 0x04, 0x01, 0x02, 0x03]);
            var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package)));
            var plan = new LaunchPlanBuilder().Build(
                [new PackageRuntimeDescriptor("third-party", PackageKind.JavaAgent, package)],
                WeaveRuntimeMode.Disabled);

            _ = NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, root);

            var after = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package)));
            Assert.Equal(before, after);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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


    [Theory]
    [InlineData("bad\"option")]
    [InlineData("bad\toption")]
    [InlineData("bad\noption")]
    [InlineData("bad\roption")]
    [InlineData("bad\0option")]
    public void LaunchPlanBuilder_RejectsUnsafeAgentOptions(string option)
    {
        var plan = new LaunchPlan(
            WeaveRuntimeMode.Disabled,
            [],
            [new LaunchAgent(@"C:\packages\agent.jar", option, "agent-a", LaunchAgentRole.PackageAgent)],
            [],
            []);

        Assert.Throws<ArgumentException>(() =>
            NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, @"C:\runtime\mods"));
    }

    [Fact]
    public void LaunchPlanBuilder_RejectsQuotedJvmPropertyValues()
    {
        var plan = new LaunchPlan(
            WeaveRuntimeMode.Disabled,
            [],
            [],
            [],
            [new LaunchJvmProperty("example.mode", "bad\"value")]);

        Assert.Throws<ArgumentException>(() =>
            NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, @"C:\runtime\mods"));
    }

    [Fact]
    public void LaunchPlanSerializerRejectsBridgeManagedWeaveProperties()
    {
        var plan = new LaunchPlan(
            WeaveRuntimeMode.Disabled,
            [],
            [new LaunchAgent(@"C:\packages\agent.jar", null, "agent-a", LaunchAgentRole.PackageAgent)],
            [],
            [new LaunchJvmProperty("weave.mods.directory", @"C:\override")]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, @"C:\runtime\mods"));

        Assert.Contains("managed by the Moonrise bridge", exception.Message, StringComparison.OrdinalIgnoreCase);
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
