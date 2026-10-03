using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LunarAttemptLifecycleTests
{
    [Fact]
    public void ManualLunarOpenedAfterLaunchRootExitIsNotAdopted()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var processes = new MutableProcesses([
            new(20, 1, "Lunar Client", launcherPath, now.AddSeconds(1), "lunarclient")
        ]);
        var windows = new TestWindows([
            new((nint)200, 20, true, "Chrome_WidgetWin_1", "Lunar Client")
        ]);
        var service = new LunarBackgroundLaunchService(
            Identity(10, launcherPath, now),
            processes,
            windows);

        Assert.Equal(0, service.HideOwnedWindows());
        Assert.Empty(service.OwnedProcessIds);
        Assert.Empty(windows.Hidden);
    }

    [Fact]
    public void ReusedPidDoesNotKeepLaunchOwnership()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var processes = new MutableProcesses([
            new(10, 1, "Lunar Client", launcherPath, now)
        ]);
        var windows = new TestWindows([
            new((nint)100, 10, true, "Chrome_WidgetWin_1", "Lunar Client")
        ]);
        var audit = new List<string>();
        var service = new LunarBackgroundLaunchService(
            Identity(10, launcherPath, now),
            processes,
            windows,
            audit: audit.Add);

        Assert.Equal(1, service.HideOwnedWindows());
        windows.Hidden.Clear();
        windows.SetVisible((nint)100, true);
        processes.Entries[0] = new ProcessTreeEntry(
            10,
            1,
            "Lunar Client",
            launcherPath,
            now.AddMinutes(1));

        Assert.Equal(0, service.HideOwnedWindows());
        Assert.Empty(service.OwnedProcessIds);
        Assert.Empty(windows.Hidden);
        Assert.Contains(audit, line =>
            line.Contains("PID reuse", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MinecraftDetectionEndsWatcherAndReleasesOwnership()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var processes = new MutableProcesses([
            new(10, 1, "Lunar Client", launcherPath, now)
        ]);
        var windows = new TestWindows([
            new((nint)100, 10, true, "Chrome_WidgetWin_1", "Lunar Client")
        ]);
        var events = new FakeWindowEvents();
        var service = new LunarBackgroundLaunchService(
            Identity(10, launcherPath, now),
            processes,
            windows,
            events);
        var watch = service.WatchAsync(CancellationToken.None);

        service.MarkMinecraftDetected();
        await watch.WaitAsync(TimeSpan.FromSeconds(2));
        var hiddenAfterCompletion = windows.Hidden.Count;
        windows.SetVisible((nint)100, true);
        events.Raise((nint)100);

        Assert.Equal(LunarLaunchWindowState.MinecraftDetected, service.State);
        Assert.Empty(service.OwnedProcessIds);
        Assert.Equal(hiddenAfterCompletion, windows.Hidden.Count);
    }

    [Fact]
    public void CancelReleasesOwnershipWithoutTerminatingProcesses()
    {
        var now = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine("C:\\", "Programs", "Lunar Client", "Lunar Client.exe");
        var processes = new MutableProcesses([
            new(10, 1, "Lunar Client", launcherPath, now)
        ]);
        var windows = new TestWindows([
            new((nint)100, 10, true, "Chrome_WidgetWin_1", "Lunar Client")
        ]);
        var service = new LunarBackgroundLaunchService(
            Identity(10, launcherPath, now),
            processes,
            windows);

        service.Cancel();

        Assert.Equal(LunarLaunchWindowState.Cancelled, service.State);
        Assert.Empty(service.OwnedProcessIds);
        Assert.Single(processes.Entries);
        Assert.Empty(windows.Hidden);
    }

    private static LunarLaunchIdentity Identity(
        int pid,
        string launcherPath,
        DateTimeOffset launchUtc) =>
        new(
            launcherPath,
            pid,
            launchUtc,
            launchUtc,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetDirectoryName(launcherPath)!
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFileName(launcherPath),
                Path.GetFileNameWithoutExtension(launcherPath)
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "lunarclient"
            });

    private sealed class MutableProcesses(IEnumerable<ProcessTreeEntry> entries) : IProcessTreeSnapshot
    {
        public List<ProcessTreeEntry> Entries { get; } = [.. entries];
        public IReadOnlyList<ProcessTreeEntry> Capture() => Entries.ToArray();
    }

    private sealed class TestWindows(IEnumerable<WindowEntry> entries) : IWindowOperations
    {
        private readonly List<WindowEntry> _entries = [.. entries];
        public List<nint> Hidden { get; } = [];
        public IReadOnlyList<WindowEntry> Enumerate() => _entries.ToArray();

        public bool Hide(nint handle)
        {
            Hidden.Add(handle);
            SetVisible(handle, false);
            return true;
        }

        public bool IsVisible(nint handle) =>
            _entries.FirstOrDefault(entry => entry.Handle == handle)?.IsVisible == true;

        public void Restore(nint handle) => SetVisible(handle, true);

        public bool Focus(nint handle) => true;

        public void SetVisible(nint handle, bool visible)
        {
            var index = _entries.FindIndex(entry => entry.Handle == handle);
            if (index >= 0)
                _entries[index] = _entries[index] with { IsVisible = visible };
        }
    }

    private sealed class FakeWindowEvents : IWindowEventSource
    {
        private Action<nint>? _handler;

        public IDisposable Subscribe(Action<nint> windowCreatedOrShown)
        {
            _handler = windowCreatedOrShown;
            return new CallbackDisposable(() => _handler = null);
        }

        public void Raise(nint handle) => _handler?.Invoke(handle);
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
