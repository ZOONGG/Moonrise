using Moonrise.Services;
using Xunit;
namespace Moonrise.Tests;
public sealed class OfficialLunarJvmLifecycleTests
{
    [Fact]
    public void EarlyCrashWinsOverWindowAndDeadlineAndStaysTerminal()
    {
        var outcome = new MinecraftLaunchOutcome();
        Assert.Equal("java-exited-nonzero", outcome.Decide(1, true, true, true));
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal("java-exited-nonzero", outcome.Decide(0, true, true, true));
        Assert.Equal(1, outcome.ExitCode);
    }
    [Fact]
    public void ShortLivedJvmIsCapturedWithoutProcessSnapshot()
    {
        var signals = new OfficialLunarJvmSignals("unused", 0);
        signals.Feed("[Launch] Starting new session (ID: 70ec5734-4a86-4e38-9a1b-232a24753214)\n[Launch] JVM process started with following outputs...\n * PID: 22808\n");
        signals.Feed("[Launch] JVM for launch session 70ec5734-4a86-4e38-9a1b-232a24753214 has exited...\n * Co");
        Assert.Null(signals.ExitCode);
        signals.Feed("de: 1\n");
        Assert.Equal(22808, signals.ProcessId);
        Assert.Equal("java-exited-nonzero", new MinecraftLaunchOutcome().Decide(signals.ExitCode, false, false, true));
    }
    [Fact]
    public void WindowConfirmsSuccessfulLaunchAtDeadline()
    {
        var outcome = new MinecraftLaunchOutcome();
        Assert.Equal("launched", outcome.Decide(null, true, true, true));
        Assert.Equal("launched", outcome.Decide(1, false, true, true));
    }
    [Fact]
    public void CleanExitNeverCreatesCrashEvenImmediately()
    {
        Assert.False(MinecraftLaunchOutcome.IsCrash(0));
        Assert.False(MinecraftLaunchOutcome.IsCrash(null));
        Assert.True(MinecraftLaunchOutcome.IsCrash(1));
        Assert.Equal("normal-exit", new MinecraftLaunchOutcome().Decide(0, false, true, true));
    }
    [Fact]
    public void TimeoutRequiresNoJvmEvidence()
    {
        Assert.Equal("minecraft-timeout", new MinecraftLaunchOutcome().Decide(null, false, true, false));
        Assert.Equal("java-startup-unconfirmed", new MinecraftLaunchOutcome().Decide(null, false, true, true));
    }
    [Fact]
    public void ExitFromBeforeCheckpointCannotBeAdopted()
    {
        var signals = new OfficialLunarJvmSignals("unused", 0);
        signals.Feed("[Launch] JVM for launch session old has exited...\n * Code: 1\n");
        Assert.Null(signals.ExitCode);
        Assert.Null(signals.ProcessId);
    }

    [Fact]
    public void PreviousSessionExitDoesNotTerminateCurrentJvm()
    {
        var signals = new OfficialLunarJvmSignals("unused", 0);
        signals.Feed("[Launch] Starting new session (ID: 70ec5734-4a86-4e38-9a1b-232a24753214)\n" +
            "[Launch] JVM process started with following outputs...\n * PID: 22808\n" +
            "[Launch] JVM for launch session 00000000-0000-0000-0000-000000000000 has exited...\n * Code: 1\n");
        Assert.Null(signals.ExitCode);
    }
}
