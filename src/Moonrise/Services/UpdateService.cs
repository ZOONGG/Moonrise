using Velopack;
using Velopack.Sources;

namespace Moonrise.Services;

// Shared exclusion across asynchronous Moonrise operations and the restart handoff.
public sealed class UpdateActivityGate
{
    private readonly object _sync = new();
    private int _operations;
    private bool _applying;
    public IDisposable EnterOperation()
    {
        lock (_sync)
        {
            if (_applying) throw new InvalidOperationException("An update restart is in progress.");
            _operations++;
            return new Lease(() => { lock (_sync) _operations--; });
        }
    }
    public string? BlockReason { get; private set; }
    public IDisposable? TryEnterApply(Func<bool> gameActive)
    {
        lock (_sync)
        {
            BlockReason = _applying ? "restart-in-progress" : gameActive() ? "game-or-launch-active"
                : _operations != 0 ? "moonrise-operation-active" : null;
            if (BlockReason is not null) return null;
            _applying = true;
            return new Lease(() => { lock (_sync) _applying = false; });
        }
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

public sealed class UpdateService(UpdateManager? manager, UpdateActivityGate activity, Func<bool> gameActive)
{
    internal Action<VelopackAsset>? ApplyOverride { get; set; }
    public string? RestartBlockReason { get; private set; }
    public const string RepositoryUrl = "https://github.com/ZOONGG/Moonrise";
    public const string Channel = "beta";
    private readonly SemaphoreSlim _serial = new(1, 1);
    public static bool IsFriendPortableBuild
    {
        get
        {
#if MOONRISE_FRIEND_PORTABLE
            return true;
#else
            return false;
#endif
        }
    }
    public bool IsEnabled => !IsFriendPortableBuild && manager is { IsInstalled: true, IsPortable: false };
    public VelopackAsset? PendingRestart => IsEnabled && manager!.UpdatePendingRestart is { } pending
        && pending.Version > manager.CurrentVersion && pending.Version.ToString().Contains("-beta.", StringComparison.Ordinal)
        ? pending : null;

    public async Task<UpdateInfo?> CheckAndDownloadAsync(bool autoDownload, Action<UpdateInfo>? found = null,
        Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || PendingRestart is not null) return null;
        var update = await CheckAsync();
        if (update is null) return null;
        found?.Invoke(update);
        if (autoDownload) await DownloadAsync(update, progress, cancellationToken);
        return update;
    }

    public static UpdateManager? CreateGithubManager()
    {
        if (IsFriendPortableBuild) return null;
        var localFeed = typeof(UpdateService).Assembly.GetCustomAttributes(
            typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MoonriseLocalUpdateFeed")?.Value;
        if (!string.IsNullOrWhiteSpace(localFeed))
            return new UpdateManager(new SimpleFileSource(new DirectoryInfo(localFeed)),
                new UpdateOptions { ExplicitChannel = Channel, AllowVersionDowngrade = false });
        return new(
        new GithubSource(RepositoryUrl, accessToken: null, prerelease: true),
        new UpdateOptions { ExplicitChannel = Channel, AllowVersionDowngrade = false });
    }

    // Velopack compares semantic versions (including beta.N), verifies package hashes,
    // and stages only in its installation packages directory, never in user data.
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!IsEnabled) return null;
        await _serial.WaitAsync();
        try
        {
            var update = await manager!.CheckForUpdatesAsync();
            return update is not null && update.TargetFullRelease.Version > manager.CurrentVersion
                && update.TargetFullRelease.Version.ToString().Contains("-beta.", StringComparison.Ordinal) ? update : null;
        }
        finally { _serial.Release(); }
    }

    public async Task DownloadAsync(UpdateInfo update, Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return;
        await _serial.WaitAsync(cancellationToken);
        try
        {
            if (update.TargetFullRelease.Version <= manager!.CurrentVersion
                || !update.TargetFullRelease.Version.ToString().Contains("-beta.", StringComparison.Ordinal))
                throw new InvalidOperationException("Only newer beta updates may be staged.");
            if (PendingRestart?.Version >= update.TargetFullRelease.Version) return;
            await manager.DownloadUpdatesAsync(update, progress, cancellationToken);
        }
        finally { _serial.Release(); }
    }

    // Call on the WPF dispatcher. The guard and handoff are synchronous, so no UI
    // operation can start between the activity check and the process exit.
    public bool TryApplyAndRestart(Action? beforeRestart = null, Action? restartFailed = null)
    {
        RestartBlockReason = null;
        if (!IsEnabled) { RestartBlockReason = "updates-disabled"; return false; }
        if (!_serial.Wait(0)) { RestartBlockReason = "update-operation-active"; return false; }
        try
        {
            var pending = PendingRestart;
            if (pending is null) { RestartBlockReason = "no-pending-update"; return false; }
            using var lease = activity.TryEnterApply(gameActive);
            if (lease is null) { RestartBlockReason = activity.BlockReason; return false; }
            beforeRestart?.Invoke();
            try
            {
                if (ApplyOverride is { } apply) apply(pending);
                else manager!.ApplyUpdatesAndRestart(pending);
            }
            catch
            {
                restartFailed?.Invoke();
                throw;
            }
            return true;
        }
        finally { _serial.Release(); }
    }

    // Metadata only: unrelated Java applications must not block an update.
    // Also covers a Lunar JVM surviving a Moonrise restart, before PID tracking starts.
    public static bool HasMinecraftProcess() => new ProcessInspector().Snapshot().Values.Any(IsMinecraftProcess);

    internal static bool IsMinecraftProcess(Moonrise.Models.ProcessRecord process)
    {
        if (!string.Equals(process.Name, "java", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(process.Name, "javaw", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(process.Name, "java.exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(process.Name, "javaw.exe", StringComparison.OrdinalIgnoreCase)) return false;
        return LunarLaunchLifecycleService.IsLikelyMinecraftProcess(new LunarManagedProcess(
            process.ProcessId, process.Name, process.ExecutablePath, process.MainWindowHandle,
            process.MainWindowTitle, process.IsMainWindowVisible, process.StartTimeUtc)) ||
            (process.ExecutablePath?.Replace('\\', '/').Contains("/.lunarclient/", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
