using System.Diagnostics;
using System.Runtime.InteropServices;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class MinecraftDetectionDelayTests
{
    [Fact]
    public void LightweightSnapshotPreservesProcessIdentityAndAncestry()
    {
        using var current = Process.GetCurrentProcess();
        var snapshot = new ProcessInspector().Snapshot(minecraftWindowsOnly: true);
        var record = snapshot[current.Id];
        Assert.Equal(new DateTimeOffset(current.StartTime.ToUniversalTime()), record.StartTimeUtc);
        Assert.True(record.ParentProcessId > 0);
        Assert.Null(record.ExecutablePath);
        Assert.Equal(0, record.MainWindowHandle);
    }

    [Fact]
    public async Task UnresponsiveWindowProbeHasBoundedWait()
    {
        var ready = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var window = CreateWindowExW(0, "STATIC", "", 0, 0, 0, 100, 100, 0, 0, 0, 0);
            ready.SetResult(window);
            stop.Wait(); // Deliberately do not pump this window's messages.
            DestroyWindow(window);
        }) { IsBackground = true };
        thread.Start();
        try
        {
            var window = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(0, window);
            var timer = Stopwatch.StartNew();
            var responding = await Task.Run(() => ProcessInspector.IsWindowResponding(window));
            Assert.False(responding);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Probe took {timer.ElapsedMilliseconds} ms");
        }
        finally { stop.Set(); thread.Join(); }
    }

    [Fact]
    public void RuntimePidWithNonGameWindowCannotConfirmSuccess()
    {
        var start = DateTimeOffset.UtcNow;
        var process = new Moonrise.Models.ProcessRecord(42, "javaw", null, 42, "Bootstrap",
            true, true, 1, start, "ConsoleWindowClass");
        var tracker = new MinecraftLaunchProcessTracker();
        tracker.ObserveRuntimeProcess(process, start);
        for (var i = 0; i < 10; i++) Assert.Null(tracker.ObserveUsableWindow([process]));
    }

    [Fact]
    public void ResponsiveWindowConfirmsWithoutWaitingForDeadlineAndResetsAfterUnresponsiveSample()
    {
        var start = DateTimeOffset.UtcNow;
        var game = new Moonrise.Models.ProcessRecord(42, "javaw", null, 42, "Minecraft",
            true, true, 1, start, "LWJGL");
        var tracker = new MinecraftLaunchProcessTracker();
        tracker.ObserveRuntimeProcess(game, start);
        for (var i = 0; i < 5; i++) Assert.Null(tracker.ObserveUsableWindow([game]));
        Assert.Null(tracker.ObserveUsableWindow([game with { IsResponding = false }]));
        for (var i = 0; i < 5; i++) Assert.Null(tracker.ObserveUsableWindow([game]));
        var outcome = new MinecraftLaunchOutcome();
        var state = new LaunchUiState();
        state.Begin("current");
        Assert.Equal(game, tracker.ObserveUsableWindow([game]));
        Assert.Equal("launched", outcome.Decide(null, true, false, true));
        Assert.True(state.Running("current"));
        Assert.False(state.Waiting);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string title,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);
}
