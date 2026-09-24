using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class UniversalLaunchPlanTests
{
    [Fact]
    public void DisabledWeave_AllowsOrderedJavaAgentsWithoutInjectingWeave()
    {
        var first = Agent("agent-a", @"C:\packages\a.jar", "alpha=1");
        var second = Agent("agent-b", @"C:\packages\b.jar", "beta=two");

        var plan = new LaunchPlanBuilder().Build(
            [first, second],
            WeaveRuntimeMode.Disabled);

        Assert.Equal(WeaveRuntimeMode.Disabled, plan.WeaveMode);
        Assert.Empty(plan.ModPaths);
        Assert.Equal(
            [@"C:\packages\a.jar", @"C:\packages\b.jar"],
            plan.Agents.Select(item => item.Path).ToArray());
        Assert.Equal("alpha=1", plan.Agents[0].Options);
        Assert.Equal("beta=two", plan.Agents[1].Options);
        Assert.All(plan.Agents, item => Assert.False(item.IsWeaveLoader));
    }

    [Fact]
    public void CurrentWeave_PrependsLoaderAndPreservesExactModAndAgentOrder()
    {
        var firstMod = Mod("mod-a", @"C:\packages\mod-a.jar");
        var secondMod = Mod("mod-b", @"C:\packages\mod-b.jar");
        var firstAgent = Agent("agent-a", @"C:\packages\agent-a.jar", null);
        var secondAgent = Agent("agent-b", @"C:\packages\agent-b.jar", "mode=strict");

        var plan = new LaunchPlanBuilder().Build(
            [firstMod, firstAgent, secondMod, secondAgent],
            WeaveRuntimeMode.Current,
            @"C:\runtime\Weave-Loader-Agent.jar");

        Assert.Equal(
            [@"C:\packages\mod-a.jar", @"C:\packages\mod-b.jar"],
            plan.ModPaths);
        Assert.Equal(3, plan.Agents.Count);
        Assert.True(plan.Agents[0].IsWeaveLoader);
        Assert.Equal(@"C:\runtime\Weave-Loader-Agent.jar", plan.Agents[0].Path);
        Assert.Equal("agent-a", plan.Agents[1].PackageId);
        Assert.Equal("agent-b", plan.Agents[2].PackageId);
        Assert.Equal("mode=strict", plan.Agents[2].Options);
    }

    [Fact]
    public void DisabledWeave_RejectsWeaveModsInsteadOfSilentlyAddingALoader()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LaunchPlanBuilder().Build(
                [Mod("mod-a", @"C:\packages\mod-a.jar")],
                WeaveRuntimeMode.Disabled));

        Assert.Contains("Weave runtime is disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomWeave_UsesTheExplicitLoaderPath()
    {
        var plan = new LaunchPlanBuilder().Build(
            [],
            WeaveRuntimeMode.Custom,
            @"C:\custom\my-weave-loader.jar");

        var loader = Assert.Single(plan.Agents);
        Assert.True(loader.IsWeaveLoader);
        Assert.Equal(@"C:\custom\my-weave-loader.jar", loader.Path);
        Assert.Equal(WeaveRuntimeMode.Custom, plan.WeaveMode);
    }

    [Fact]
    public void UnsupportedRuntimeKindFailsClosed()
    {
        var package = new PackageRuntimeDescriptor(
            "unknown",
            PackageKind.Unclassified,
            @"C:\packages\unknown.jar");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LaunchPlanBuilder().Build([package], WeaveRuntimeMode.Disabled));

        Assert.Contains("unsupported runtime kind", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JvmArgumentsAndPropertiesRemainOrderedAndExplicit()
    {
        var plan = new LaunchPlanBuilder().Build(
            [],
            WeaveRuntimeMode.Disabled,
            jvmArguments: ["-Xmx4G", "-XX:+UseG1GC"],
            jvmProperties:
            [
                new LaunchJvmProperty("moonrise.test", "one"),
                new LaunchJvmProperty("example.mode", "two")
            ]);

        Assert.Equal(["-Xmx4G", "-XX:+UseG1GC"], plan.JvmArguments);
        Assert.Equal(["moonrise.test", "example.mode"], plan.JvmProperties.Select(item => item.Name).ToArray());
        Assert.Equal(["one", "two"], plan.JvmProperties.Select(item => item.Value).ToArray());
    }

    [Fact]
    public void DuplicatePackageIdFailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LaunchPlanBuilder().Build(
                [
                    Agent("same", @"C:\packages\a.jar", null),
                    Agent("SAME", @"C:\packages\b.jar", null)
                ],
                WeaveRuntimeMode.Disabled));

        Assert.Contains("appears more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateRuntimePathFailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LaunchPlanBuilder().Build(
                [
                    Agent("agent-a", @"C:\packages\agent.jar", null),
                    Agent("agent-b", @"c:\PACKAGES\agent.jar", null)
                ],
                WeaveRuntimeMode.Disabled));

        Assert.Contains("runtime path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateJvmPropertyFailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LaunchPlanBuilder().Build(
                [],
                WeaveRuntimeMode.Disabled,
                jvmProperties:
                [
                    new LaunchJvmProperty("moonrise.mode", "one"),
                    new LaunchJvmProperty("MOONRISE.MODE", "two")
                ]));

        Assert.Contains("property", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-javaagent:C:\\packages\\agent.jar")]
    [InlineData("-Dmoonrise.test=1")]
    public void StructuredJvmChannelsCannotBeSmuggledThroughRawArguments(string argument)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LaunchPlanBuilder().Build(
                [],
                WeaveRuntimeMode.Disabled,
                jvmArguments: [argument]));

        Assert.Contains("structured", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentOptionsRejectProtocolSeparators()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new LaunchPlanBuilder().Build(
                [Agent("agent-a", @"C:\packages\agent.jar", "mode\tstrict")],
                WeaveRuntimeMode.Disabled));

        Assert.Contains("unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PackageRuntimeDescriptor Agent(string id, string path, string? options) =>
        new(id, PackageKind.JavaAgent, path, options);

    private static PackageRuntimeDescriptor Mod(string id, string path) =>
        new(id, PackageKind.WeaveMod, path);
}
