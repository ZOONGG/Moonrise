using System.Xml.Linq;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LunarLaunchLifecycleTests
{
    private const string LauncherPath = @"C:\Apps\Lunar Client\Lunar Client.exe";

    [Fact]
    public void Capture_SeparatesOfficialLauncherFromMinecraftAndUnrelatedProcesses()
    {
        var backend = new FakeBackend([
            new LunarManagedProcess(10, 1.ToString(), null, 0, string.Empty, false),
            new LunarManagedProcess(11, "Lunar Client", LauncherPath, 100, "Lunar Client", true),
            new LunarManagedProcess(12, "javaw", @"C:\Java\javaw.exe", 200, "Minecraft 1.8.9", true),
            new LunarManagedProcess(13, "javaw", @"C:\Java\javaw.exe", 300, "IDE helper", true),
            new LunarManagedProcess(14, "Lunar Client", @"D:\Other\Lunar Client.exe", 400, "Lunar Client", true)
        ]);
        var service = new LunarLaunchLifecycleService(backend);

        var snapshot = service.Capture(LauncherPath);

        Assert.Equal([11], snapshot.LauncherProcessIds);
        Assert.Equal([12], snapshot.MinecraftProcessIds);
        Assert.Equal(1, snapshot.VisibleLauncherWindowCount);
    }

    [Theory]
    [InlineData(false, false, false, "Proceed")]
    [InlineData(true, false, false, "PromptToCloseLauncher")]
    [InlineData(true, false, true, "AutoCloseLauncher")]
    [InlineData(false, true, false, "BlockForMinecraft")]
    [InlineData(true, true, true, "BlockForMinecraft")]
    public void PrelaunchPolicy_ChoosesExpectedAction(
        bool launcherRunning,
        bool minecraftRunning,
        bool autoClose,
        string expected)
    {
        var snapshot = new LunarPrelaunchSnapshot(
            launcherRunning ? [10] : [],
            minecraftRunning ? [20] : [],
            launcherRunning ? 1 : 0);

        Assert.Equal(expected, LunarPrelaunchPolicy.Decide(snapshot, autoClose).ToString());
    }

    [Fact]
    public async Task CloseLauncher_UsesGracefulCloseFirst_AndLeavesMinecraftAlone()
    {
        var backend = new FakeBackend([
            new LunarManagedProcess(11, "Lunar Client", LauncherPath, 100, "Lunar Client", true),
            new LunarManagedProcess(12, "javaw", @"C:\Java\javaw.exe", 200, "Minecraft 1.8.9", true)
        ])
        {
            RemoveOnClose = true
        };
        var service = new LunarLaunchLifecycleService(backend);

        var result = await service.CloseLauncherAsync(
            LauncherPath,
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal([11], backend.CloseRequests);
        Assert.Empty(backend.ForceRequests);
        Assert.Contains(backend.Processes, process => process.ProcessId == 12);
        Assert.DoesNotContain(backend.Processes, process => process.ProcessId == 11);
    }

    [Fact]
    public async Task CloseLauncher_ForceTerminatesOnlyRemainingLunarProcesses()
    {
        var backend = new FakeBackend([
            new LunarManagedProcess(11, "Lunar Client", LauncherPath, 100, "Lunar Client", true),
            new LunarManagedProcess(12, "javaw", @"C:\Java\javaw.exe", 200, "Minecraft 1.8.9", true)
        ])
        {
            RemoveOnClose = false,
            RemoveOnForce = true
        };
        var service = new LunarLaunchLifecycleService(backend);

        var result = await service.CloseLauncherAsync(
            LauncherPath,
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal([11], backend.CloseRequests);
        Assert.Equal([11], backend.ForceRequests);
        Assert.Equal([11], result.ForceTerminatedProcessIds);
        Assert.Contains(backend.Processes, process => process.ProcessId == 12);
    }

    [Fact]
    public async Task CloseLauncher_ReportsTimeoutWithoutTouchingMinecraft()
    {
        var backend = new FakeBackend([
            new LunarManagedProcess(11, "Lunar Client", LauncherPath, 100, "Lunar Client", true),
            new LunarManagedProcess(12, "javaw", @"C:\Java\javaw.exe", 200, "Minecraft 1.8.9", true)
        ])
        {
            RemoveOnClose = false,
            RemoveOnForce = false
        };
        var service = new LunarLaunchLifecycleService(backend);

        var result = await service.CloseLauncherAsync(
            LauncherPath,
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal([11], result.RemainingProcessIds);
        Assert.Equal([11], backend.ForceRequests);
        Assert.Contains(backend.Processes, process => process.ProcessId == 12);
    }

    [Fact]
    public async Task ParallelLaunchRequests_AllowExactlyOneActiveAttempt()
    {
        var gate = 0;
        var winners = 0;
        var tasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                if (LaunchSingleFlight.TryEnter(ref gate))
                    Interlocked.Increment(ref winners);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, winners);
        Assert.True(LaunchSingleFlight.Exit(ref gate));
        Assert.False(LaunchSingleFlight.Exit(ref gate));
    }

    [Fact]
    public async Task ParallelDeeplinkRequests_SendExactlyOnce()
    {
        var sent = 0;
        var winners = 0;
        var tasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                if (LaunchSingleFlight.TrySend(ref sent))
                    Interlocked.Increment(ref winners);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, winners);
    }

    [Fact]
    public void TrustedIpcSender_IsTrackedButJavaWindowIsStillExcluded()
    {
        var processes = new FakeProcessTree([
            new ProcessTreeEntry(10, 1, "Lunar Client"),
            new ProcessTreeEntry(11, 1, "Lunar Client"),
            new ProcessTreeEntry(12, 10, "javaw")
        ]);
        var windows = new FakeWindows([
            new WindowEntry((nint)110, 11, true, "Chrome_WidgetWin_1", "Lunar Client"),
            new WindowEntry((nint)120, 12, true, "LWJGL", "Minecraft 1.8.9")
        ]);
        var service = new LunarBackgroundLaunchService(10, processes, windows);

        service.TrackTrustedProcess(11);
        service.HideOwnedWindows();

        Assert.Contains(11, service.OwnedProcessIds);
        Assert.Contains((nint)110, windows.Hidden);
        Assert.DoesNotContain((nint)120, windows.Hidden);
    }

    [Fact]
    public void MainWindow_WiresPromptAutoCloseAndSingleFlightGuards()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"AutoCloseLunarCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LunarRunningOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LunarRunningRememberCheckBox\"", xaml, StringComparison.Ordinal);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace names = "http://schemas.microsoft.com/winfx/2006/xaml";
        var overlay = XDocument.Parse(xaml).Descendants(ui + "Grid")
            .Single(element => (string?)element.Attribute(names + "Name") == "LunarRunningOverlay");
        XElement Named(string name) => overlay.Descendants()
            .Single(element => (string?)element.Attribute(names + "Name") == name);
        Assert.Equal("{StaticResource MoonriseCheckBox}", (string?)Named("LunarRunningRememberCheckBox").Attribute("Style"));
        Assert.Equal("{StaticResource DialogSecondaryButton}", (string?)Named("LunarRunningCancelButton").Attribute("Style"));
        Assert.Equal("{StaticResource DialogPrimaryButton}", (string?)Named("LunarRunningConfirmButton").Attribute("Style"));
        Assert.Equal("LunarRunningCancelButton_Click", (string?)Named("LunarRunningCancelButton").Attribute("Click"));
        Assert.Equal("LunarRunningConfirmButton_Click", (string?)Named("LunarRunningConfirmButton").Attribute("Click"));
        Assert.Contains(overlay.Descendants(ui + "ContentControl"),
            element => (string?)element.Attribute("Template") == "{StaticResource LunarClientIconTemplate}");
        Assert.Contains("PromptToCloseRunningLunarAsync", code, StringComparison.Ordinal);
        Assert.Contains("CloseLauncherAsync", code, StringComparison.Ordinal);
        Assert.Contains("LaunchSingleFlight.TryEnter", code, StringComparison.Ordinal);
        Assert.Contains("LaunchSingleFlight.TrySend", code, StringComparison.Ordinal);
        Assert.Contains("background?.TrackTrustedProcess(sender.Id)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Сначала полностью закройте Lunar Launcher", code, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Moonrise.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeBackend(IEnumerable<LunarManagedProcess> processes) : ILunarProcessBackend
    {
        public List<LunarManagedProcess> Processes { get; } = [.. processes];
        public List<int> CloseRequests { get; } = [];
        public List<int> ForceRequests { get; } = [];
        public bool RemoveOnClose { get; init; }
        public bool RemoveOnForce { get; init; }

        public IReadOnlyList<LunarManagedProcess> Snapshot() => Processes.ToArray();

        public bool RequestClose(int processId)
        {
            CloseRequests.Add(processId);
            if (RemoveOnClose)
                Processes.RemoveAll(process => process.ProcessId == processId);
            return true;
        }

        public bool ForceTerminate(int processId)
        {
            ForceRequests.Add(processId);
            if (RemoveOnForce)
                Processes.RemoveAll(process => process.ProcessId == processId);
            return true;
        }
    }

    private sealed class FakeProcessTree(IEnumerable<ProcessTreeEntry> processes) : IProcessTreeSnapshot
    {
        public IReadOnlyList<ProcessTreeEntry> Capture() => processes.ToArray();
    }

    private sealed class FakeWindows(IEnumerable<WindowEntry> windows) : IWindowOperations
    {
        private readonly List<WindowEntry> _windows = [.. windows];
        public List<nint> Hidden { get; } = [];

        public IReadOnlyList<WindowEntry> Enumerate() => _windows.ToArray();

        public bool Hide(nint handle)
        {
            Hidden.Add(handle);
            var index = _windows.FindIndex(window => window.Handle == handle);
            if (index >= 0)
                _windows[index] = _windows[index] with { IsVisible = false };
            return true;
        }

        public bool IsVisible(nint handle) =>
            _windows.FirstOrDefault(window => window.Handle == handle)?.IsVisible == true;

        public void Restore(nint handle) { }
        public bool Focus(nint handle) => true;
    }
}
