using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class MinecraftLaunchProcessTrackerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-03T19:18:21Z");
    private static ProcessRecord Process(int id, int parent, string name, int seconds,
        string windowClass = "", string title = "", bool visible = false) =>
        new(id, name, null, visible ? id : 0, title, visible, true,
            parent, Start.AddSeconds(seconds), windowClass);

    private static ProcessRecord[] Observe(MinecraftLaunchProcessTracker tracker, params ProcessRecord[] processes) =>
        tracker.Observe(processes.ToDictionary(p => p.ProcessId), new[] { 10 }, new HashSet<int>());

    [Fact]
    public async Task CleanBootstrapExitDoesNotBecomeGameFailureAndReplacementBecomesTarget()
    {
        var tracker = new MinecraftLaunchProcessTracker();
        var root = Process(10, 1, "Lunar Client", 0);
        var bootstrap = Process(20, 10, "java", 1, title: "Bootstrap", visible: true);
        Assert.Equal(20, Assert.Single(Observe(tracker, root, bootstrap)).ProcessId);
        Assert.False(tracker.IsMinecraft(bootstrap));
        for (var sample = 0; sample < 6; sample++)
            Assert.Null(tracker.ObserveUsableWindow([bootstrap]));
        var bootstrapExit = Task.FromResult<int?>(0);
        Assert.Null(tracker.ObserveGameExit(bootstrap, _ => bootstrapExit));
        Assert.Equal(0, await bootstrapExit);
        // Bootstrap exited with code 0: it has no Minecraft evidence, so the monitor
        // never arms the java-exited-before-usable-window failure for its exit.
        Assert.Empty(Observe(tracker, root));
        Assert.False(tracker.SawMinecraft);
        Assert.Null(tracker.FailureStage(Start.AddSeconds(10), Start.AddSeconds(180), [], false));
        Assert.Null(tracker.FailureStage(Start.AddSeconds(20), Start.AddSeconds(180), [], false));
        // Retain ancestry when the replacement is observed after its parent exits.
        var game = Process(30, 20, "javaw", 30, "LWJGL", "Lunar Client 1.8.9", true);
        Assert.Equal(30, Assert.Single(Observe(tracker, root, game)).ProcessId);
        Assert.True(tracker.IsMinecraft(game));
        Assert.Equal(30, await tracker.ObserveGameExit(game, id => Task.FromResult<int?>(id))!);
        for (var sample = 0; sample < 5; sample++)
            Assert.Null(tracker.ObserveUsableWindow(Observe(tracker, root, game)));
        Assert.Equal(30, tracker.ObserveUsableWindow(Observe(tracker, root, game))!.ProcessId);
    }

    [Fact]
    public void BootstrapWithoutReplacementTimesOutAtOriginalDeadline()
    {
        var tracker = new MinecraftLaunchProcessTracker();
        var deadline = Start.AddSeconds(180);
        Observe(tracker, Process(10, 1, "Lunar Client", 0), Process(20, 10, "java", 1));
        Observe(tracker);
        Assert.Null(tracker.FailureStage(Start.AddSeconds(6), deadline, [], false));
        Assert.Null(tracker.FailureStage(deadline.AddMilliseconds(-1), deadline, [], false));
        Assert.Equal("minecraft-timeout", tracker.FailureStage(deadline, deadline, [], false));
        Assert.False(tracker.SawMinecraft);
    }

    [Fact]
    public void ActualGameExitStillFailsEvenWhenBootstrapRemainsAlive()
    {
        var tracker = new MinecraftLaunchProcessTracker();
        var root = Process(10, 1, "Lunar Client", 0);
        var bootstrap = Process(20, 10, "java", 1);
        Observe(tracker, root, bootstrap, Process(30, 20, "javaw", 2, "LWJGL"));
        var candidates = Observe(tracker, root, bootstrap);
        Assert.Null(tracker.FailureStage(Start.AddSeconds(3), Start.AddSeconds(180), candidates, true));
        Assert.Equal("java-exited-before-usable-window",
            tracker.FailureStage(Start.AddSeconds(8), Start.AddSeconds(180), candidates, true));
    }

    [Fact]
    public void UnrelatedJavaAndReusedAncestorCannotBecomeTarget()
    {
        var tracker = new MinecraftLaunchProcessTracker();
        var root = Process(10, 1, "Lunar Client", 0);
        Observe(tracker, root, Process(20, 10, "java", 1));
        var reused = Process(20, 1, "other", 10);
        var unrelated = Process(40, 1, "java", 11, "LWJGL", "Minecraft", true);
        var falseChild = Process(30, 20, "javaw", 12, "LWJGL", "Minecraft", true);
        Assert.Empty(Observe(tracker, root, reused, unrelated, falseChild));
        Assert.False(tracker.SawMinecraft);
    }

    [Fact]
    public void BlankMinecraftWindowIsGameEvidenceBeforeUsableWindow()
    {
        var tracker = new MinecraftLaunchProcessTracker();
        var game = Process(20, 10, "javaw", 1, "LWJGL", visible: true);
        Observe(tracker, Process(10, 1, "Lunar Client", 0), game);
        Assert.True(tracker.IsMinecraft(game));
        Assert.True(tracker.SawMinecraft);
        Assert.Empty(Observe(tracker, Process(10, 1, "Lunar Client", 0)));
        Assert.True(tracker.SawMinecraft);
    }
}
