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
    int OwnerProcessId = 0);

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
    private static readonly TimeSpan MinecraftPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReplacementGracePeriod = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan LaunchStartTolerance = TimeSpan.FromSeconds(5);
    private const int MaximumHideAttempts = 3;

    private readonly LunarLaunchIdentity _identity;
    private readonly IProcessTreeSnapshot _processTree;
    private readonly IWindowOperations _windows;
    private readonly IWindowEventSource _windowEvents;
    private readonly Action<string>? _audit;
    private readonly HashSet<int> _ownedProcessIds = [];
    private readonly HashSet<string> _knownExecutableNames;
    private readonly HashSet<string> _auditedWindowStates = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private LunarLaunchWindowState _state = LunarLaunchWindowState.BackgroundHidden;
    private bool _keepHiddenAfterMinecraft;
    private DateTimeOffset _lastTrustedProcessSeenUtc;

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
        _knownExecutableNames = new HashSet<string>(
            identity.KnownExecutableNames,
            StringComparer.OrdinalIgnoreCase);
        _lastTrustedProcessSeenUtc = identity.LaunchTimestampUtc;
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
                RefreshOwnedProcessIds(_processTree.Capture());
                return _ownedProcessIds.ToArray();
            }
        }
    }

    public async Task WatchAsync(CancellationToken cancellationToken)
    {
        IDisposable? subscription = null;
        try
        {
            try
            {
                subscription = _windowEvents.Subscribe(HandleWindowCreatedOrShown);
            }
            catch (Exception exception) when (
                exception is Win32Exception or PlatformNotSupportedException)
            {
                Audit($"Lunar window hook unavailable: {exception.GetType().Name}; polling remains active.");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                LunarLaunchWindowState state;
                bool keepHidden;
                lock (_sync)
                {
                    state = _state;
                    keepHidden = _keepHiddenAfterMinecraft;
                }

                if (state is LunarLaunchWindowState.UserVisible or
                    LunarLaunchWindowState.InteractionRequired or
                    LunarLaunchWindowState.LauncherExited or
                    LunarLaunchWindowState.Cancelled)
                {
                    return;
                }
                if (state == LunarLaunchWindowState.MinecraftDetected && !keepHidden)
                    return;

                if (!IsLauncherTreeRunning())
                {
                    SetState(LunarLaunchWindowState.LauncherExited);
                    return;
                }

                HideOwnedWindows();
                var delay = state == LunarLaunchWindowState.BackgroundHidden
                    ? BackgroundPollInterval
                    : MinecraftPollInterval;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStateIfHiding(LunarLaunchWindowState.Cancelled);
        }
        finally
        {
            subscription?.Dispose();
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
            _state = LunarLaunchWindowState.UserVisible;
            _keepHiddenAfterMinecraft = false;
            return RevealAndFocusCore();
        }
    }

    public bool RequireInteraction()
    {
        lock (_sync)
        {
            _state = LunarLaunchWindowState.InteractionRequired;
            _keepHiddenAfterMinecraft = false;
            return RevealAndFocusCore();
        }
    }

    public bool RevealAndFocus() => ShowLunar();

    public void MarkMinecraftDetected()
    {
        lock (_sync)
        {
            _keepHiddenAfterMinecraft = _state == LunarLaunchWindowState.BackgroundHidden;
            _state = LunarLaunchWindowState.MinecraftDetected;
        }
        HideOwnedWindows();
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _state = LunarLaunchWindowState.Cancelled;
            _keepHiddenAfterMinecraft = false;
        }
    }

    public bool IsLauncherTreeRunning()
    {
        lock (_sync)
        {
            var running = _processTree.Capture();
            RefreshOwnedProcessIds(running);
            if (running.Any(process => _ownedProcessIds.Contains(process.ProcessId)))
                return true;
            return _identity.InstallationDirectories.Count > 0 &&
                DateTimeOffset.UtcNow - _lastTrustedProcessSeenUtc < ReplacementGracePeriod;
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
            .Where(window => ClassifyWindow(window, processes).IsLunar)
            .OrderByDescending(window => window.ProcessId == _identity.InitialProcessId)
            .ThenByDescending(window => window.IsVisible)
            .ToArray();
        foreach (var window in candidates)
            _windows.Restore(window.Handle);
        return candidates.Length > 0 && _windows.Focus(candidates[0].Handle);
    }

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
        if (_ownedProcessIds.Contains(window.ProcessId))
            return new WindowClassification(true, "included: trusted-lunar-process");
        if (window.OwnerProcessId > 0 && _ownedProcessIds.Contains(window.OwnerProcessId))
        {
            _ownedProcessIds.Add(window.ProcessId);
            RememberExecutable(process);
            return new WindowClassification(true, "included: owner-is-trusted-lunar-process");
        }

        var pathInInstall = IsInKnownInstallationDirectory(process.ExecutablePath);
        var recent = IsCurrentLaunchProcess(process);
        var knownName = IsKnownExecutableName(process);
        var safeMarker = HasSafeLunarCommandLineMarker(process.CommandLine);
        var lunarWindow = IsObservedLunarWindow(window);
        if (pathInInstall && recent && (knownName || safeMarker) && lunarWindow)
        {
            _ownedProcessIds.Add(window.ProcessId);
            RememberExecutable(process);
            _lastTrustedProcessSeenUtc = DateTimeOffset.UtcNow;
            return new WindowClassification(
                true,
                "included: detached-replacement(path+start+name/marker+window)");
        }
        return new WindowClassification(
            false,
            $"excluded: signals[path={pathInInstall},recent={recent},knownName={knownName}," +
            $"marker={safeMarker},lunarWindow={lunarWindow}]");
    }

    private void RefreshOwnedProcessIds(IReadOnlyList<ProcessTreeEntry> snapshot)
    {
        var runningIds = snapshot.Select(process => process.ProcessId).ToHashSet();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var process in snapshot)
            {
                if (_ownedProcessIds.Contains(process.ProcessId))
                {
                    RememberExecutable(process);
                    continue;
                }
                if (IsExplicitlyExcluded(process, null))
                    continue;

                var ancestor = _ownedProcessIds.Contains(process.ParentProcessId);
                var exactPath = PathsEqual(process.ExecutablePath, _identity.InitialExecutablePath);
                var pathInInstall = IsInKnownInstallationDirectory(process.ExecutablePath);
                var recent = IsCurrentLaunchProcess(process);
                var knownName = IsKnownExecutableName(process);
                var safeMarker = HasSafeLunarCommandLineMarker(process.CommandLine);
                if (ancestor ||
                    (exactPath && recent && knownName) ||
                    (pathInInstall && recent && knownName && safeMarker))
                {
                    changed |= _ownedProcessIds.Add(process.ProcessId);
                    RememberExecutable(process);
                    if (!ancestor)
                    {
                        Audit(
                            $"Detached Lunar process tracked: pid={process.ProcessId}; parentPid={process.ParentProcessId}; " +
                            $"startUtc={process.StartTimeUtc:O}; executable={SafeExecutableName(process)}; " +
                            $"pathHash={HashPath(process.ExecutablePath)}; " +
                            $"reason={(exactPath ? "exact-path+name+start" : "install-path+name+start+marker")}");
                    }
                }
            }
        }
        _ownedProcessIds.RemoveWhere(processId => !runningIds.Contains(processId));
        if (_ownedProcessIds.Count > 0)
            _lastTrustedProcessSeenUtc = DateTimeOffset.UtcNow;
    }

    private bool ShouldHideWindows() =>
        _state == LunarLaunchWindowState.BackgroundHidden ||
        (_state == LunarLaunchWindowState.MinecraftDetected && _keepHiddenAfterMinecraft);

    private void SetState(LunarLaunchWindowState state)
    {
        lock (_sync)
            _state = state;
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
            $"{classification.Reason}|{hideResult}";
        if (!_auditedWindowStates.Add(stateKey))
            return;
        Audit(
            $"Lunar window candidate: pid={window.ProcessId}; parentPid={parentPid}; " +
            $"startUtc={start}; executable={executable}; pathHash={pathHash}; " +
            $"class={SanitizeClass(window.ClassName)}; title={sanitizedTitle}; " +
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
            result.Add(new WindowEntry(
                handle,
                checked((int)processId),
                IsWindowVisible(handle),
                GetClass(handle),
                GetTitle(handle),
                checked((int)ownerProcessId)));
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
