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
        Assert.Equal(["alpha=1", "beta=two"], plan.Agents.Select(item => item.Options).ToArray());
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

    private static PackageRuntimeDescriptor Agent(string id, string path, string? options) =>
        new(id, PackageKind.JavaAgent, path, options);

    private static PackageRuntimeDescriptor Mod(string id, string path) =>
        new(id, PackageKind.WeaveMod, path);
}
