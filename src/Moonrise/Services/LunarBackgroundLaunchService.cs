using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Moonrise.Services;

public enum LunarLaunchWindowState
{
    BackgroundHidden,
    UserVisible,
    InteractionRequired,
    MinecraftDetected,
    LauncherExited,
    Cancelled
}

internal sealed record ProcessTreeEntry(
    int ProcessId,
    int ParentProcessId,
    string Name,
    string? ExecutablePath = null,
    DateTimeOffset? StartTimeUtc = null,
    string? CommandLine = null);

internal sealed record WindowEntry(
    nint Handle,
    int ProcessId,
    bool IsVisible,
    string ClassName = "",
    string Title = "",
    int OwnerProcessId = 0,
    nint OwnerHandle = 0,
    int Width = 0,
    int Height = 0,
    long Style = 0,
    long ExtendedStyle = 0);

internal sealed record LunarLaunchIdentity(
    string InitialExecutablePath,
    int InitialProcessId,
    DateTimeOffset InitialProcessStartUtc,
    DateTimeOffset LaunchTimestampUtc,
    IReadOnlySet<string> InstallationDirectories,
    IReadOnlySet<string> KnownExecutableNames,
    IReadOnlySet<string> SafeCommandLineMarkers);

internal interface IProcessTreeSnapshot
{
    IReadOnlyList<ProcessTreeEntry> Capture();
}

internal interface IWindowOperations
{
    IReadOnlyList<WindowEntry> Enumerate();
    bool Hide(nint handle);
    bool IsVisible(nint handle);
    void Restore(nint handle);
    bool Focus(nint handle);
}

internal interface IWindowEventSource
{
    IDisposable Subscribe(Action<nint> windowCreatedOrShown);
}

public sealed class LunarBackgroundLaunchService
{
    private static readonly TimeSpan BackgroundPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ProcessStartTolerance = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LaunchStartTolerance = TimeSpan.FromSeconds(5);
    private const int MaximumHideAttempts = 3;

    private readonly LunarLaunchIdentity _identity;
    private readonly IProcessTreeSnapshot _processTree;
    private readonly IWindowOperations _windows;
    private readonly IWindowEventSource _windowEvents;
    private readonly Action<string>? _audit;
    private readonly HashSet<int> _ownedProcessIds = [];
    private readonly Dictionary<int, DateTimeOffset?> _ownedProcessStartTimes = [];
    private readonly HashSet<string> _knownExecutableNames;
    private readonly HashSet<string> _auditedWindowStates = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _watchStop = new();
    private bool _released;
    private bool _watchStarted;
    private LunarLaunchWindowState _state = LunarLaunchWindowState.BackgroundHidden;

    public LunarBackgroundLaunchService(int rootProcessId)
        : this(
            CreateFallbackIdentity(rootProcessId),
            new WindowsProcessTreeSnapshot(),
            new WindowsWindowOperations(),
            new WindowsWindowEventSource(),
            null)
    {
    }

    public LunarBackgroundLaunchService(
        int rootProcessId,
        string launcherPath,
        DateTimeOffset launchTimestampUtc,
        Action<string>? audit = null)
        : this(
            CaptureIdentity(rootProcessId, launcherPath, launchTimestampUtc),
            new WindowsProcessTreeSnapshot(),
            new WindowsWindowOperations(),
            new WindowsWindowEventSource(),
            audit)
    {
    }

    internal LunarBackgroundLaunchService(
        int rootProcessId,
        IProcessTreeSnapshot processTree,
        IWindowOperations windows,
        IWindowEventSource? windowEvents = null)
        : this(
            CreateFallbackIdentity(rootProcessId),
            processTree,
            windows,
            windowEvents ?? new NoWindowEventSource(),
            null)
    {
    }

    internal LunarBackgroundLaunchService(
        LunarLaunchIdentity identity,
        IProcessTreeSnapshot processTree,
        IWindowOperations windows,
        IWindowEventSource? windowEvents = null,
        Action<string>? audit = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (identity.InitialProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(identity));
        _processTree = processTree ?? throw new ArgumentNullException(nameof(processTree));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _windowEvents = windowEvents ?? new NoWindowEventSource();
        _audit = audit;
        _ownedProcessIds.Add(identity.InitialProcessId);
        _ownedProcessStartTimes[identity.InitialProcessId] = identity.InitialProcessStartUtc;
        _knownExecutableNames = new HashSet<string>(
            identity.KnownExecutableNames,
            StringComparer.OrdinalIgnoreCase);
        Audit(
            $"Lunar launch identity: initialPid={identity.InitialProcessId}; " +
            $"initialStartUtc={identity.InitialProcessStartUtc:O}; launchUtc={identity.LaunchTimestampUtc:O}; " +
            $"executable={Path.GetFileName(identity.InitialExecutablePath)}; " +
            $"pathHash={HashPath(identity.InitialExecutablePath)}");
    }

    public LunarLaunchWindowState State
    {
        get
        {
            lock (_sync)
                return _state;
        }
    }

    public IReadOnlyCollection<int> OwnedProcessIds
    {
        get
        {
            lock (_sync)
            {
                if (!_released) RefreshOwnedProcessIds(_processTree.Capture());
                return _ownedProcessIds.ToArray();
            }
        }
    }

    public void TrackTrustedProcess(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        lock (_sync)
        {
            if (_released) return;
            var process = _processTree.Capture()
                .FirstOrDefault(item => item.ProcessId == processId);
            if (process is null)
            {
                Audit($"Trusted Lunar IPC process was not adopted because it was not observed: pid={processId}");
                return;
            }
            if (_ownedProcessIds.Add(processId))
                Audit($"Trusted Lunar IPC process tracked: pid={processId}");
            _ownedProcessStartTimes[processId] = process.StartTimeUtc;
        }
    }

    public async Task WatchAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _watchStop.Token);
        cancellationToken = linked.Token;
        IDisposable? subscription = null;
        lock (_sync)
        {
            if (_released || _watchStarted || cancellationToken.IsCancellationRequested) return;
            _watchStarted = true;
        }
        try
        {
            try
            {
                lock (_sync)
                {
                    if (_released || cancellationToken.IsCancellationRequested) return;
                    subscription = _windowEvents.Subscribe(HandleWindowCreatedOrShown);
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception or PlatformNotSupportedException)
            {
                Audit($"Lunar window hook unavailable: {exception.GetType().Name}; polling remains active.");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                LunarLaunchWindowState state;
                lock (_sync)
                    state = _state;

                if (state is LunarLaunchWindowState.UserVisible or
                    LunarLaunchWindowState.InteractionRequired or
                    LunarLaunchWindowState.MinecraftDetected or
                    LunarLaunchWindowState.LauncherExited or
                    LunarLaunchWindowState.Cancelled)
                {
                    return;
                }

                if (!IsLauncherTreeRunning())
                {
                    SetState(LunarLaunchWindowState.LauncherExited);
                    return;
                }

                HideOwnedWindows();
                await Task.Delay(BackgroundPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStateIfHiding(LunarLaunchWindowState.Cancelled);
        }
        finally
        {
            subscription?.Dispose();
            lock (_sync)
            {
                if (cancellationToken.IsCancellationRequested ||
                    _state is LunarLaunchWindowState.MinecraftDetected or
                        LunarLaunchWindowState.LauncherExited or LunarLaunchWindowState.Cancelled)
                    ReleaseOwnershipCore("watcher-ended");
            }
        }
    }

    public int HideOwnedWindows()
    {
        lock (_sync)
        {
            if (!ShouldHideWindows())
                return 0;
            var snapshot = _processTree.Capture();
            RefreshOwnedProcessIds(snapshot);
            var processes = snapshot.ToDictionary(item => item.ProcessId);
            var hidden = 0;
            foreach (var window in _windows.Enumerate())
            {
                var classification = ClassifyWindow(window, processes);
                if (classification.IsLunar) _ = IsMainLauncherWindow(window, processes);
                var hideResult = "not-requested";
                if (window.IsVisible && classification.IsLunar)
                {
                    var success = HideWithBoundedRetry(window.Handle, out var attempts);
                    hideResult = success
                        ? $"hidden;attempts={attempts}"
                        : $"failed-visible;attempts={attempts}";
                    if (success)
                        hidden++;
                }
                AuditWindow(window, processes.GetValueOrDefault(window.ProcessId), classification, hideResult);
            }
            return hidden;
        }
    }

    public bool ShowLunar()
    {
        lock (_sync)
        {
            if (_released) return false;
            _state = LunarLaunchWindowState.UserVisible;
            return RevealAndFocusCore();
        }
    }

    public bool RequireInteraction()
    {
        lock (_sync)
        {
            if (_released) return false;
            _state = LunarLaunchWindowState.InteractionRequired;
            return RevealAndFocusCore();
        }
    }

    public bool RevealAndFocus() => ShowLunar();

    public void MarkMinecraftDetected()
    {
        lock (_sync)
        {
            if (_released) return;
            _state = LunarLaunchWindowState.MinecraftDetected;
            ReleaseOwnershipCore("minecraft-detected");
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (_released) return;
            _state = LunarLaunchWindowState.Cancelled;
            ReleaseOwnershipCore("cancelled");
        }
    }

    public bool IsLauncherTreeRunning()
    {
        lock (_sync)
        {
            if (_released) return false;
            RefreshOwnedProcessIds(_processTree.Capture());
            return _ownedProcessIds.Count > 0;
        }
    }

    private void HandleWindowCreatedOrShown(nint handle)
    {
        lock (_sync)
        {
            if (!ShouldHideWindows())
                return;
            var snapshot = _processTree.Capture();
            RefreshOwnedProcessIds(snapshot);
            var processes = snapshot.ToDictionary(item => item.ProcessId);
            var window = _windows.Enumerate().FirstOrDefault(item => item.Handle == handle);
            if (window is null)
                return;
            var classification = ClassifyWindow(window, processes);
            if (classification.IsLunar) _ = IsMainLauncherWindow(window, processes);
            var hideResult = "not-requested";
            if (window.IsVisible && classification.IsLunar)
            {
                var success = HideWithBoundedRetry(handle, out var attempts);
                hideResult = success
                    ? $"hidden;attempts={attempts}"
                    : $"failed-visible;attempts={attempts}";
            }
            AuditWindow(window, processes.GetValueOrDefault(window.ProcessId), classification, hideResult);
        }
    }

    private bool RevealAndFocusCore()
    {
        var snapshot = _processTree.Capture();
        RefreshOwnedProcessIds(snapshot);
        var processes = snapshot.ToDictionary(item => item.ProcessId);
        var candidates = _windows.Enumerate()
            .Where(window => IsMainLauncherWindow(window, processes))
            .OrderByDescending(window => window.ProcessId == _identity.InitialProcessId)
            .ThenByDescending(window => window.IsVisible)
            .ToArray();
        if (candidates.Length == 0) return false;
        _windows.Restore(candidates[0].Handle);
        return _windows.Focus(candidates[0].Handle);
    }

    private bool IsMainLauncherWindow(WindowEntry window, IReadOnlyDictionary<int, ProcessTreeEntry> processes)
    {
        var ownership = ClassifyWindow(window, processes);
        var process = processes.GetValueOrDefault(window.ProcessId);
        var reason = !ownership.IsLunar ? ownership.Reason
            : process is null || !IsLunarExecutable(process) ? "rejected: not-launcher-executable"
            : window.OwnerHandle != 0 || window.OwnerProcessId != 0 ? "rejected: owned-window"
            : (window.ExtendedStyle & 0x80) != 0 || (window.Style & 0x40000000) != 0 ? "rejected: tool-or-child-window"
            : !string.Equals(window.ClassName, "Chrome_WidgetWin_1", StringComparison.Ordinal) ? "rejected: helper-window-class"
            : window.Width < 400 || window.Height < 250 ? "rejected: invalid-main-window-size"
            : "accepted: verified-launcher-main-window";
        var accepted = reason.StartsWith("accepted:", StringComparison.Ordinal);
        // The confirmed main launcher can be hidden by background launch. Hidden helper
        // windows never qualify merely through process ancestry or their title.
        AuditWindow(window, process, new WindowClassification(accepted, reason), "main-window-selection");
        return accepted;
    }

    private bool IsLunarExecutable(ProcessTreeEntry process) =>
        process.ExecutablePath is { } path &&
        PathsEqual(path, _identity.InitialExecutablePath) &&
        !(process.CommandLine?.Contains("--type=", StringComparison.OrdinalIgnoreCase) ?? false);

    private bool HideWithBoundedRetry(nint handle, out int attempts)
    {
        for (attempts = 1; attempts <= MaximumHideAttempts; attempts++)
        {
            _ = _windows.Hide(handle);
            if (!_windows.IsVisible(handle))
                return true;
        }
        attempts = MaximumHideAttempts;
        return false;
    }

    private WindowClassification ClassifyWindow(
        WindowEntry window,
        IReadOnlyDictionary<int, ProcessTreeEntry> processes)
    {
        if (!processes.TryGetValue(window.ProcessId, out var process))
            return new WindowClassification(false, "excluded: process-not-running");
        if (IsExplicitlyExcluded(process, window))
            return new WindowClassification(false, $"excluded: {ExplicitExclusionReason(process, window)}");
        if (IsOwnedProcess(process))
            return new WindowClassification(true, "included: verified-launch-process");
        return new WindowClassification(false, "excluded: not-a-verified-launch-descendant");
    }

    private void RefreshOwnedProcessIds(IReadOnlyList<ProcessTreeEntry> snapshot)
    {
        if (_released) return;
        var processes = snapshot.ToDictionary(process => process.ProcessId);

        foreach (var ownedProcessId in _ownedProcessIds.ToArray())
        {
            if (!processes.TryGetValue(ownedProcessId, out var process))
            {
                _ownedProcessIds.Remove(ownedProcessId);
                _ownedProcessStartTimes.Remove(ownedProcessId);
                continue;
            }
            _ = IsOwnedProcess(process);
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var process in snapshot)
            {
                if (IsOwnedProcess(process) || IsExplicitlyExcluded(process, null))
                    continue;
                if (!processes.TryGetValue(process.ParentProcessId, out var parent) ||
                    !IsOwnedProcess(parent))
                {
                    continue;
                }

                if (_ownedProcessIds.Add(process.ProcessId))
                {
                    _ownedProcessStartTimes[process.ProcessId] = process.StartTimeUtc;
                    changed = true;
                    Audit(
                        $"Lunar launch descendant tracked: pid={process.ProcessId}; parentPid={process.ParentProcessId}; " +
                        $"startUtc={process.StartTimeUtc:O}; executable={SafeExecutableName(process)}; " +
                        $"pathHash={HashPath(process.ExecutablePath)}");
                }
            }
        }
    }

    private bool IsOwnedProcess(ProcessTreeEntry process)
    {
        if (!_ownedProcessIds.Contains(process.ProcessId))
            return false;
        if (!_ownedProcessStartTimes.TryGetValue(process.ProcessId, out var expectedStart) ||
            expectedStart is null ||
            process.StartTimeUtc is null)
        {
            return true;
        }

        if ((process.StartTimeUtc.Value - expectedStart.Value).Duration() <= ProcessStartTolerance)
            return true;

        _ownedProcessIds.Remove(process.ProcessId);
        _ownedProcessStartTimes.Remove(process.ProcessId);
        Audit(
            $"Lunar process ownership released after PID reuse: pid={process.ProcessId}; " +
            $"expectedStartUtc={expectedStart:O}; actualStartUtc={process.StartTimeUtc:O}");
        return false;
    }

    private void ReleaseOwnershipCore(string reason)
    {
        if (_ownedProcessIds.Count > 0)
            Audit($"Lunar launch ownership released: reason={reason}; processCount={_ownedProcessIds.Count}");
        _ownedProcessIds.Clear();
        _ownedProcessStartTimes.Clear();
        _released = true;
        _auditedWindowStates.Clear();
        _watchStop.Cancel();
    }

    private bool ShouldHideWindows() =>
        !_released && _state == LunarLaunchWindowState.BackgroundHidden;

    private void SetState(LunarLaunchWindowState state)
    {
        lock (_sync)
            if (!_released) _state = state;
    }

    private void SetStateIfHiding(LunarLaunchWindowState state)
    {
        lock (_sync)
        {
            if (ShouldHideWindows())
                _state = state;
        }
    }

    private bool IsCurrentLaunchProcess(ProcessTreeEntry process) =>
        process.StartTimeUtc is { } start &&
        start >= _identity.LaunchTimestampUtc - LaunchStartTolerance;

    private bool IsKnownExecutableName(ProcessTreeEntry process)
    {
        var fileName = !string.IsNullOrWhiteSpace(process.ExecutablePath)
            ? Path.GetFileName(process.ExecutablePath)
            : process.Name;
        return _knownExecutableNames.Contains(fileName) ||
            _knownExecutableNames.Contains(Path.GetFileNameWithoutExtension(fileName));
    }

    private bool IsInKnownInstallationDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        string directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        }
        catch
        {
            return false;
        }
        return _identity.InstallationDirectories.Any(root =>
            string.Equals(
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                directory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));
    }

    private bool HasSafeLunarCommandLineMarker(string? commandLine) =>
        !string.IsNullOrWhiteSpace(commandLine) &&
        _identity.SafeCommandLineMarkers.Any(marker =>
            commandLine.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsObservedLunarWindow(WindowEntry window) =>
        window.ClassName.StartsWith("Chrome_WidgetWin_", StringComparison.OrdinalIgnoreCase) ||
        window.Title.Contains("Lunar", StringComparison.OrdinalIgnoreCase);

    private static bool IsExplicitlyExcluded(ProcessTreeEntry process, WindowEntry? window)
    {
        var name = Path.GetFileNameWithoutExtension(process.Name);
        if (name.Equals("java", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("javaw", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Moonrise", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (window is not null &&
            window.Title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    private static string ExplicitExclusionReason(ProcessTreeEntry process, WindowEntry? window)
    {
        if (window is not null &&
            window.Title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase))
        {
            return "minecraft-window";
        }
        return $"{Path.GetFileNameWithoutExtension(process.Name).ToLowerInvariant()}-process";
    }

    private void RememberExecutable(ProcessTreeEntry process)
    {
        if (!string.IsNullOrWhiteSpace(process.Name))
            _knownExecutableNames.Add(process.Name);
        if (!string.IsNullOrWhiteSpace(process.ExecutablePath))
            _knownExecutableNames.Add(Path.GetFileName(process.ExecutablePath));
    }

    private void AuditWindow(
        WindowEntry window,
        ProcessTreeEntry? process,
        WindowClassification classification,
        string hideResult)
    {
        var parentPid = process?.ParentProcessId ?? 0;
        var start = process?.StartTimeUtc?.ToString("O") ?? "unknown";
        var executable = process is null ? "unknown" : SafeExecutableName(process);
        var pathHash = HashPath(process?.ExecutablePath);
        var sanitizedTitle = SanitizeTitle(window.Title);
        var stateKey =
            $"{window.Handle}|{window.ProcessId}|{window.IsVisible}|{classification.IsLunar}|" +
            $"{classification.Reason}|{hideResult}|{window.ClassName}|{window.OwnerHandle}|" +
            $"{window.Width}|{window.Height}|{window.Style}|{window.ExtendedStyle}";
        if (!_auditedWindowStates.Add(stateKey))
            return;
        Audit(
            $"Lunar window candidate: pid={window.ProcessId}; parentPid={parentPid}; " +
            $"startUtc={start}; executable={executable}; pathHash={pathHash}; " +
            $"class={SanitizeClass(window.ClassName)}; title={sanitizedTitle}; " +
            $"hwnd=0x{window.Handle:X}; ownerHwnd=0x{window.OwnerHandle:X}; " +
            $"dimensions={window.Width}x{window.Height}; style=0x{window.Style:X}; extendedStyle=0x{window.ExtendedStyle:X}; " +
            $"ownerPid={window.OwnerProcessId}; visible={window.IsVisible}; " +
            $"classifiedLunar={classification.IsLunar}; reason={classification.Reason}; hide={hideResult}");
    }

    private void Audit(string message)
    {
        try
        {
            _audit?.Invoke(TokenRedactor.Redact(message));
        }
        catch
        {
        }
    }

    private static string SafeExecutableName(ProcessTreeEntry process)
    {
        var name = !string.IsNullOrWhiteSpace(process.ExecutablePath)
            ? Path.GetFileName(process.ExecutablePath)
            : process.Name;
        return string.IsNullOrWhiteSpace(name) ? "unknown" : Path.GetFileName(name);
    }

    private static string SanitizeClass(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "empty"
            : value.Length <= 96
                ? value
                : value[..96];

    private static string SanitizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "empty";
        if (title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase))
            return "Minecraft";
        if (title.Contains("Lunar", StringComparison.OrdinalIgnoreCase))
            return "Lunar Client";
        return "redacted:" + HashText(title)[..12];
    }

    private static string HashPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "unknown";
        try
        {
            return HashText(Path.GetFullPath(path).ToUpperInvariant())[..16];
        }
        catch
        {
            return "invalid";
        }
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static LunarLaunchIdentity CaptureIdentity(
        int processId,
        string launcherPath,
        DateTimeOffset launchTimestampUtc)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        var fullPath = Path.GetFullPath(launcherPath);
        var start = launchTimestampUtc;
        try
        {
            using var process = Process.GetProcessById(processId);
            start = process.StartTime.ToUniversalTime();
        }
        catch
        {
        }
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Lunar executable path has no installation directory.");
        return new LunarLaunchIdentity(
            fullPath,
            processId,
            start,
            launchTimestampUtc,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { directory },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFileName(fullPath),
                Path.GetFileNameWithoutExtension(fullPath)
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "lunarclient",
                "lunar client",
                "lunarclient://"
            });
    }

    private static LunarLaunchIdentity CreateFallbackIdentity(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        var now = DateTimeOffset.UtcNow;
        return new LunarLaunchIdentity(
            "Lunar Client.exe",
            processId,
            now,
            now,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Lunar Client",
                "Lunar Client.exe"
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lunarclient" });
    }

    private sealed record WindowClassification(bool IsLunar, string Reason);

    private sealed class NoWindowEventSource : IWindowEventSource
    {
        public IDisposable Subscribe(Action<nint> windowCreatedOrShown) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        internal static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}

internal static class OfficialLunarStartInfoFactory
{
    internal static ProcessStartInfo Create(
        string launcherPath,
        bool background,
        string? launcherArgument = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = launcherPath,
            WorkingDirectory = Path.GetDirectoryName(launcherPath),
            UseShellExecute = false,
            CreateNoWindow = background,
            WindowStyle = background ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        };
        if (!string.IsNullOrWhiteSpace(launcherArgument))
            startInfo.ArgumentList.Add(launcherArgument);
        return startInfo;
    }
}

internal sealed class WindowsProcessTreeSnapshot : IProcessTreeSnapshot
{
    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly nint InvalidHandleValue = new(-1);

    public IReadOnlyList<ProcessTreeEntry> Capture()
    {
        var result = new List<ProcessTreeEntry>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandleValue)
            return result;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
                return result;
            do
            {
                var processId = checked((int)entry.ProcessId);
                string? executablePath = null;
                DateTimeOffset? startTimeUtc = null;
                try
                {
                    using var process = Process.GetProcessById(processId);
                    executablePath = process.MainModule?.FileName;
                    startTimeUtc = process.StartTime.ToUniversalTime();
                }
                catch (Exception exception) when (
                    exception is ArgumentException or InvalidOperationException or
                    Win32Exception or NotSupportedException)
                {
                }
                result.Add(new ProcessTreeEntry(
                    processId,
                    checked((int)entry.ParentProcessId),
                    Path.GetFileNameWithoutExtension(entry.ExecutableFile),
                    executablePath,
                    startTimeUtc));
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            } while (Process32Next(snapshot, ref entry));
            return result;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

internal sealed class WindowsWindowOperations : IWindowOperations
{
    private const int SwHide = 0;
    private const int SwRestore = 9;
    private const uint GwOwner = 4;

    public IReadOnlyList<WindowEntry> Enumerate()
    {
        var result = new List<WindowEntry>();
        EnumWindows((handle, parameter) =>
        {
            _ = GetWindowThreadProcessId(handle, out var processId);
            var ownerHandle = GetWindow(handle, GwOwner);
            var ownerProcessId = 0u;
            if (ownerHandle != 0)
                _ = GetWindowThreadProcessId(ownerHandle, out ownerProcessId);
            _ = GetWindowRect(handle, out var rectangle);
            result.Add(new WindowEntry(
                handle,
                checked((int)processId),
                IsWindowVisible(handle),
                GetClass(handle),
                GetTitle(handle),
                checked((int)ownerProcessId), ownerHandle,
                rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top,
                GetWindowLongPtrW(handle, -16).ToInt64(), GetWindowLongPtrW(handle, -20).ToInt64()));
            return true;
        }, 0);
        return result;
    }

    public bool Hide(nint handle)
    {
        _ = ShowWindow(handle, SwHide);
        return !IsWindowVisible(handle);
    }

    public bool IsVisible(nint handle) => IsWindowVisible(handle);
    public void Restore(nint handle) => _ = ShowWindow(handle, SwRestore);
    public bool Focus(nint handle) => SetForegroundWindow(handle);

    private static string GetClass(nint handle)
    {
        var builder = new StringBuilder(256);
        return GetClassName(handle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    private static string GetTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
            return string.Empty;
        var builder = new StringBuilder(Math.Min(length + 1, 1024));
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsCallback(nint windowHandle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out WindowRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint handle, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint windowHandle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder title, int maximumCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}

internal sealed class WindowsWindowEventSource : IWindowEventSource
{
    public IDisposable Subscribe(Action<nint> windowCreatedOrShown) =>
        new Subscription(windowCreatedOrShown);

    private sealed class Subscription : IDisposable
    {
        private const uint EventObjectCreate = 0x8000;
        private const uint EventObjectShow = 0x8002;
        private const uint EventObjectStateChange = 0x800A;
        private const uint WineventOutofcontext = 0x0000;
        private const int ObjidWindow = 0;
        private readonly WinEventDelegate _callback;
        private nint _hook;

        internal Subscription(Action<nint> handler)
        {
            _callback = (_, eventType, window, objectId, childId, _, _) =>
            {
                if (window != 0 &&
                    objectId == ObjidWindow &&
                    childId == 0 &&
                    eventType is EventObjectCreate or EventObjectShow or EventObjectStateChange)
                {
                    handler(window);
                }
            };
            _hook = SetWinEventHook(
                EventObjectCreate,
                EventObjectStateChange,
                0,
                _callback,
                0,
                0,
                WineventOutofcontext);
            if (_hook == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            var hook = Interlocked.Exchange(ref _hook, 0);
            if (hook != 0)
                _ = UnhookWinEvent(hook);
            GC.KeepAlive(_callback);
        }
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}
