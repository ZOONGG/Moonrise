using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LunarAttemptTerminationTests
{
    [Fact]
    public void ConstructionDoesNotSubscribeOrManipulateWindows()
    {
        var fixture = new Fixture();
        Assert.Equal(0, fixture.Events.Subscriptions);
        Assert.Empty(fixture.Windows.Hidden);
        Assert.Empty(fixture.Windows.Restored);
    }

    [Fact]
    public async Task MinecraftDetectionStopsWatcherAndLeavesLaterManualLunarUntouched()
    {
        var f = new Fixture();
        var watch = f.Service.WatchAsync(CancellationToken.None);
        f.Service.MarkMinecraftDetected();
        await watch.WaitAsync(TimeSpan.FromSeconds(2));
        f.AssertReleased();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("finished")]
    public async Task AttemptTeardownStopsWatcherAndReleasesOwnership(string reason)
    {
        var f = new Fixture();
        var watch = f.Service.WatchAsync(CancellationToken.None);
        // MainWindow releases the active background service through Cancel.
        Assert.NotEmpty(reason);
        f.Service.Cancel();
        await watch.WaitAsync(TimeSpan.FromSeconds(2));
        f.AssertReleased();
        await f.Service.WatchAsync(CancellationToken.None);
        Assert.Equal(1, f.Events.Subscriptions);
    }

    [Fact]
    public async Task ExternalCancellationReleasesWatcher()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var watch = f.Service.WatchAsync(cancellation.Token);
        cancellation.Cancel();
        await watch.WaitAsync(TimeSpan.FromSeconds(2));
        f.AssertReleased();
    }

    [Fact]
    public void MatchingPathAndWindowOwnerDoNotProveAttemptOwnership()
    {
        var f = new Fixture();
        f.Processes.Items.Add(new(20, 1, "Lunar Client", f.Path, f.Start.AddSeconds(1), "lunarclient"));
        f.Windows.Items.Add(new((nint)200, 20, true, "Chrome_WidgetWin_1", "Lunar Client", 10));
        Assert.Equal(1, f.Service.HideOwnedWindows());
        Assert.DoesNotContain((nint)200, f.Windows.Hidden);
        f.Service.ShowLunar();
        Assert.DoesNotContain((nint)200, f.Windows.Restored);
    }

    [Fact]
    public void ReusedRootPidIsNeverHiddenOrRestored()
    {
        var f = new Fixture();
        f.Processes.Items[0] = f.Processes.Items[0] with { StartTimeUtc = f.Start.AddMinutes(1) };
        Assert.Equal(0, f.Service.HideOwnedWindows());
        Assert.False(f.Service.ShowLunar());
        Assert.Empty(f.Windows.Restored);
    }

    [Fact]
    public void VerifiedDescendantsAreScopedToAttemptAndJavaIsUntouched()
    {
        var f = new Fixture();
        f.Processes.Items.Add(new(11, 10, "Lunar Client", f.Path, f.Start.AddSeconds(1)));
        f.Processes.Items.Add(new(12, 11, "javaw", "javaw.exe", f.Start.AddSeconds(2)));
        f.Windows.Items.Add(new((nint)110, 11, true));
        f.Windows.Items.Add(new((nint)120, 12, true, "LWJGL", "Minecraft"));
        Assert.Equal(2, f.Service.HideOwnedWindows());
        f.Service.ShowLunar();
        Assert.DoesNotContain((nint)120, f.Windows.Hidden);
        Assert.DoesNotContain((nint)120, f.Windows.Restored);
        Assert.Equal(3, f.Processes.Items.Count);
    }

    private sealed class Fixture
    {
        public readonly string Path = System.IO.Path.GetFullPath("Lunar Client.exe");
        public readonly DateTimeOffset Start = DateTimeOffset.UtcNow;
        public readonly Processes Processes = new();
        public readonly Windows Windows = new();
        public readonly Events Events = new();
        public readonly LunarBackgroundLaunchService Service;
        public Fixture()
        {
            Processes.Items.Add(new(10, 1, "Lunar Client", Path, Start));
            Windows.Items.Add(new((nint)100, 10, true));
            Service = new(new LunarLaunchIdentity(Path, 10, Start, Start,
                new HashSet<string> { System.IO.Path.GetDirectoryName(Path)! },
                new HashSet<string> { "Lunar Client.exe" }, new HashSet<string> { "lunarclient" }), Processes, Windows, Events);
        }
        public void AssertReleased()
        {
            Assert.False(Events.Active);
            Assert.Empty(Service.OwnedProcessIds);
            var hides = Windows.Hidden.Count;
            Processes.Items.Add(new(20, 1, "Lunar Client", Path, Start.AddSeconds(5)));
            Windows.Items.Add(new((nint)200, 20, true, "Chrome_WidgetWin_1", "Lunar Client"));
            Events.Raise((nint)200);
            Assert.Equal(0, Service.HideOwnedWindows());
            Assert.False(Service.ShowLunar());
            Assert.False(Service.RequireInteraction());
            Assert.Equal(hides, Windows.Hidden.Count);
            Assert.Empty(Windows.Restored);
        }
    }
    private sealed class Processes : IProcessTreeSnapshot
    {
        public List<ProcessTreeEntry> Items = [];
        public IReadOnlyList<ProcessTreeEntry> Capture() => Items.ToArray();
    }
    private sealed class Windows : IWindowOperations
    {
        public List<WindowEntry> Items = [];
        public List<nint> Hidden = [], Restored = [];
        public IReadOnlyList<WindowEntry> Enumerate() => Items.ToArray();
        public bool Hide(nint handle) { Hidden.Add(handle); return true; }
        public bool IsVisible(nint handle) => false;
        public void Restore(nint handle) => Restored.Add(handle);
        public bool Focus(nint handle) => true;
    }
    private sealed class Events : IWindowEventSource
    {
        private Action<nint>? _handler;
        public int Subscriptions;
        public bool Active => _handler is not null;
        public IDisposable Subscribe(Action<nint> handler) { Subscriptions++; _handler = handler; return new Subscription(() => _handler = null); }
        public void Raise(nint handle) => _handler?.Invoke(handle);
        private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}
