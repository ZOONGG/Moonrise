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
    public async Task CloseLauncher_DoesNotForceTerminatePreexistingLunarProcesses()
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

        Assert.False(result.Success);
        Assert.Equal([11], backend.CloseRequests);
        Assert.Empty(backend.ForceRequests);
        Assert.Empty(result.ForceTerminatedProcessIds);
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
        Assert.Empty(backend.ForceRequests);
        Assert.Contains(backend.Processes, process => process.ProcessId == 12);
    }

    [Fact]
    public async Task ThreeSequentialHiddenLaunchers_CloseWithoutForceOrAccumulation()
    {
        var backend = new FakeBackend([]) { RemoveOnClose = true };
        var service = new LunarLaunchLifecycleService(backend);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.False(service.Capture(LauncherPath).LauncherRunning);
            backend.Processes.Add(new(attempt, "Lunar Client", LauncherPath, 0, "", false));
            var result = await service.CloseLauncherAsync(LauncherPath, TimeSpan.Zero,
                TimeSpan.Zero, CancellationToken.None);
            Assert.True(result.Success);
            Assert.False(service.Capture(LauncherPath).LauncherRunning);
        }
        Assert.Equal([1, 2, 3], backend.CloseRequests);
        Assert.Empty(backend.ForceRequests);
    }

    [Fact]
    public async Task NewlyAppearingLauncher_IsReportedAndNeverClosedOrKilled()
    {
        var backend = new FakeBackend([new(11, "Lunar Client", LauncherPath, 0, "", false)])
        {
            RemoveOnClose = true,
            OnClose = processes => processes.Add(new(22, "Lunar Client", LauncherPath, 0, "", false))
        };
        var result = await new LunarLaunchLifecycleService(backend).CloseLauncherAsync(
            LauncherPath, TimeSpan.Zero, TimeSpan.Zero, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal([22], result.RemainingProcessIds);
        Assert.Equal([11], backend.CloseRequests);
        Assert.Empty(backend.ForceRequests);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task OwnedSenderFallback_OnlyForcesAfterGracefulTimeout(
        bool alreadyExited, bool closesGracefully, bool expectedForce)
    {
        var calls = new List<string>();
        var forced = await LunarLaunchLifecycleService.CloseOwnedSenderAsync(
            () => alreadyExited,
            () => calls.Add("close"),
            _ => { calls.Add("wait"); return Task.FromResult(closesGracefully); },
            () => calls.Add("force"), TimeSpan.Zero);
        Assert.Equal(expectedForce, forced);
        if (alreadyExited) Assert.Empty(calls);
        else if (expectedForce) Assert.Equal(new[] { "close", "wait", "force", "wait" }, calls);
        else Assert.Equal(new[] { "close", "wait" }, calls);
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
        Assert.Equal("{StaticResource DialogRememberCheckBox}", (string?)Named("LunarRunningRememberCheckBox").Attribute("Style"));
        Assert.Equal("{StaticResource DialogSecondaryButton}", (string?)Named("LunarRunningCancelButton").Attribute("Style"));
        Assert.Equal("{StaticResource DialogPrimaryButton}", (string?)Named("LunarRunningConfirmButton").Attribute("Style"));
        Assert.Equal("560", (string?)Named("LunarRunningDialogPanel").Attribute("Width"));
        Assert.Equal("16", (string?)Named("LunarRunningDetail").Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)Named("LunarRunningDetail").Attribute("FontWeight"));
        Assert.Equal("24", (string?)Named("LunarRunningDetail").Attribute("LineHeight"));
        Assert.Equal("15", (string?)Named("LunarRunningRememberCheckBox").Attribute("FontSize"));
        var document = XDocument.Parse(xaml);
        XElement Style(string key) => document.Descendants(ui + "Style")
            .Single(element => (string?)element.Attribute(names + "Key") == key);
        Assert.Equal("{StaticResource ButtonBase}", (string?)Style("DialogSecondaryButton").Attribute("BasedOn"));
        Assert.DoesNotContain(Style("DialogSecondaryButton").Elements(ui + "Setter"),
            element => (string?)element.Attribute("Property") == "Template");
        Assert.Equal("{StaticResource PrimaryButton}", (string?)Style("DialogPrimaryButton").Attribute("BasedOn"));
        var expectedTemplate = new XElement(Style("PrimaryButton").Descendants(ui + "ControlTemplate").Single());
        expectedTemplate.Descendants().Single(element => (string?)element.Attribute(names + "Name") == "EnergySweep").Remove();
        foreach (var animation in expectedTemplate.Descendants().Where(element =>
                     (string?)element.Attribute("Storyboard.TargetName") == "EnergySweep").ToArray())
            animation.Remove();
        Assert.True(XNode.DeepEquals(expectedTemplate, Style("DialogPrimaryButton").Descendants(ui + "ControlTemplate").Single()));
        foreach (var key in new[] { "DialogPrimaryButton", "ButtonBase" })
        {
            Assert.DoesNotContain(Style(key).Descendants(), element => element.Name.LocalName is "TranslateTransform" or "ScaleTransform");
            Assert.DoesNotContain(Style(key).Descendants().Attributes(), attribute => attribute.Value.Contains("RenderTransform", StringComparison.Ordinal));
        }
        var checkboxTemplate = Style("DialogRememberCheckBox").Descendants(ui + "ControlTemplate").Single();
        Assert.All(checkboxTemplate.Descendants().Where(element => element.Attribute("VerticalAlignment") is not null),
            element => Assert.Equal("Center", (string?)element.Attribute("VerticalAlignment")));
        Assert.Equal("LunarRunningCancelButton_Click", (string?)Named("LunarRunningCancelButton").Attribute("Click"));
        Assert.Equal("LunarRunningConfirmButton_Click", (string?)Named("LunarRunningConfirmButton").Attribute("Click"));
        Assert.Contains(overlay.Descendants(ui + "ContentControl"),
            element => (string?)element.Attribute("Template") == "{StaticResource LunarClientIconTemplate}");
        Assert.Contains("PromptToCloseRunningLunarAsync", code, StringComparison.Ordinal);
        Assert.Contains("QuitControlledLauncherAsync", code, StringComparison.Ordinal);
        Assert.Contains("LaunchSingleFlight.TryEnter", code, StringComparison.Ordinal);
        Assert.Contains("LaunchSingleFlight.TrySend", code, StringComparison.Ordinal);
        Assert.Contains("background?.TrackTrustedProcess(sender.Id)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("РЎРЅР°С‡Р°Р»Р° РїРѕР»РЅРѕСЃС‚СЊСЋ Р·Р°РєСЂРѕР№С‚Рµ Lunar Launcher", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ControlledQuit_ThreeCyclesUseQuitWithoutWindowCloseOrKill()
    {
        var backend = new FakeBackend([]);
        var service = new LunarLaunchLifecycleService(backend);
        var sends = 0;
        for (var pid = 1; pid <= 3; pid++)
        {
            var start = DateTimeOffset.UtcNow;
            backend.Processes.Add(new(pid, "Lunar Client", LauncherPath, 0, "", false, start));
            backend.Processes.Add(new(pid + 100, "Lunar Client", LauncherPath, 0, "", false, start));
            Assert.True(service.RegisterControlledLauncher(LauncherPath, pid));
            var tree = new FakeProcessTree([new(pid, 0, "Lunar Client", LauncherPath, start),
                new(pid + 100, pid, "Lunar Client", LauncherPath, start)]);
            var result = await service.CleanupAfterGameAsync(LauncherPath, TimeSpan.Zero,
                CancellationToken.None, tree, () => { sends++; backend.Processes.Clear(); return Task.FromResult(true); });
            Assert.True(result.Success);
            Assert.Empty(service.Capture(LauncherPath).LauncherProcessIds);
        }
        Assert.Equal(3, sends);
        Assert.Empty(backend.CloseRequests);
        Assert.Empty(backend.ForceRequests);
    }

    [Theory]
    [InlineData("unowned")]
    [InlineData("pid-reused")]
    [InlineData("foreign-root")]
    [InlineData("foreign-installation")]
    [InlineData("minecraft")]
    [InlineData("missing-tree")]
    [InlineData("hidden-java")]
    public async Task ControlledQuit_BlocksAmbiguousOwnershipWithoutSending(string scenario)
    {
        var start = DateTimeOffset.UtcNow;
        var backend = new FakeBackend([new(11, "Lunar Client", LauncherPath, 0, "", false, start)]);
        var service = new LunarLaunchLifecycleService(backend);
        if (scenario != "unowned") Assert.True(service.RegisterControlledLauncher(LauncherPath, 11));
        if (scenario == "pid-reused") backend.Processes[0] = backend.Processes[0] with { StartTimeUtc = start.AddSeconds(1) };
        if (scenario == "foreign-root") backend.Processes.Add(new(12, "Lunar Client", LauncherPath, 0, "", false, start));
        if (scenario == "foreign-installation") backend.Processes.Add(new(12, "Lunar Client", @"D:\Other\Lunar Client.exe", 0, "", false, start));
        if (scenario == "minecraft") backend.Processes.Add(new(13, "javaw", "javaw.exe", 100, "Minecraft", true, start));
        var tree = new FakeProcessTree(scenario == "missing-tree" ? [] : [new(11, 0, "Lunar Client", LauncherPath, start)]);
        if (scenario == "hidden-java") tree = new FakeProcessTree([
            new(11, 0, "Lunar Client", LauncherPath, start), new(99, 11, "javaw", "javaw.exe", start)]);
        var sent = false;
        var result = await service.CleanupAfterGameAsync(LauncherPath, TimeSpan.Zero, CancellationToken.None,
            tree, () => { sent = true; return Task.FromResult(true); });
        Assert.False(result.Success);
        Assert.False(sent);
        Assert.Empty(backend.CloseRequests);
        Assert.Empty(backend.ForceRequests);
    }

    [Fact]
    public async Task ControlledQuit_UnresponsiveLauncherFailsClosed()
    {
        var start = DateTimeOffset.UtcNow;
        var backend = new FakeBackend([new(11, "Lunar Client", LauncherPath, 0, "", false, start)]);
        var service = new LunarLaunchLifecycleService(backend);
        Assert.True(service.RegisterControlledLauncher(LauncherPath, 11));
        var tree = new FakeProcessTree([new(11, 0, "Lunar Client", LauncherPath, start)]);
        var result = await service.CleanupAfterGameAsync(LauncherPath, TimeSpan.Zero, CancellationToken.None,
            tree, () => Task.FromResult(true));
        Assert.False(result.Success);
        Assert.Equal([11], result.RemainingProcessIds);
        Assert.Empty(backend.CloseRequests);
        Assert.Empty(backend.ForceRequests);
    }

    [Fact]
    public async Task PostGameCleanupWaitsForHiddenJvmToDisappearBeforeQuit()
    {
        var start = DateTimeOffset.UtcNow;
        var backend = new FakeBackend([new(11, "Lunar Client", LauncherPath, 0, "", false, start)]);
        var service = new LunarLaunchLifecycleService(backend);
        Assert.True(service.RegisterControlledLauncher(LauncherPath, 11));
        var captures = 0;
        var tree = new ChangingProcessTree(() => ++captures == 1
            ? [new(11, 0, "Lunar Client", LauncherPath, start), new(99, 11, "javaw", "javaw.exe", start)]
            : [new(11, 0, "Lunar Client", LauncherPath, start)]);
        var sent = false;
        var result = await service.CleanupAfterGameAsync(LauncherPath, TimeSpan.FromSeconds(1),
            CancellationToken.None, tree, () =>
            {
                Assert.True(captures >= 4); // Waiting guard and two immediate dispatch guards.
                sent = true;
                backend.Processes.Clear();
                return Task.FromResult(true);
            });
        Assert.True(sent);
        Assert.True(result.Success);
        Assert.Empty(backend.CloseRequests);
        Assert.Empty(backend.ForceRequests);
    }

    private sealed class ChangingProcessTree(Func<IReadOnlyList<ProcessTreeEntry>> capture) : IProcessTreeSnapshot
    {
        public IReadOnlyList<ProcessTreeEntry> Capture() => capture();
    }

    [Fact]
    public async Task ConcurrentPostGameAndRepeatLaunchCleanupSendOnlyOneQuit()
    {
        var start = DateTimeOffset.UtcNow;
        var backend = new FakeBackend([new(11, "Lunar Client", LauncherPath, 0, "", false, start)]);
        var service = new LunarLaunchLifecycleService(backend);
        Assert.True(service.RegisterControlledLauncher(LauncherPath, 11));
        var tree = new FakeProcessTree([new(11, 0, "Lunar Client", LauncherPath, start)]);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        async Task<bool> Send()
        {
            sends++;
            await release.Task;
            backend.Processes.Clear();
            return true;
        }
        var postGame = service.CleanupAfterGameAsync(LauncherPath, TimeSpan.Zero, CancellationToken.None, tree, Send);
        var repeat = service.QuitControlledLauncherAsync(LauncherPath, TimeSpan.Zero, CancellationToken.None, tree, Send);
        release.SetResult(true);
        var results = await Task.WhenAll(postGame, repeat);
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(1, sends);
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
        public Action<List<LunarManagedProcess>>? OnClose { get; init; }

        public IReadOnlyList<LunarManagedProcess> Snapshot() => Processes.ToArray();

        public bool RequestClose(int processId)
        {
            CloseRequests.Add(processId);
            if (RemoveOnClose)
                Processes.RemoveAll(process => process.ProcessId == processId);
            OnClose?.Invoke(Processes);
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
