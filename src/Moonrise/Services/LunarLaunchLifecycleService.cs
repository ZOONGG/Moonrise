using System.Diagnostics;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed record LunarPrelaunchSnapshot(
    IReadOnlyList<int> LauncherProcessIds,
    IReadOnlyList<int> MinecraftProcessIds,
    int VisibleLauncherWindowCount)
{
    public bool LauncherRunning => LauncherProcessIds.Count > 0;
    public bool MinecraftRunning => MinecraftProcessIds.Count > 0;
}

internal enum LunarPrelaunchAction
{
    Proceed,
    PromptToCloseLauncher,
    AutoCloseLauncher,
    BlockForMinecraft
}

internal static class LunarPrelaunchPolicy
{
    internal static LunarPrelaunchAction Decide(
        LunarPrelaunchSnapshot snapshot,
        bool autoCloseLauncher) =>
        snapshot.MinecraftRunning
            ? LunarPrelaunchAction.BlockForMinecraft
            : !snapshot.LauncherRunning
                ? LunarPrelaunchAction.Proceed
                : autoCloseLauncher
                    ? LunarPrelaunchAction.AutoCloseLauncher
                    : LunarPrelaunchAction.PromptToCloseLauncher;
}

public sealed record LunarLauncherCloseResult(
    IReadOnlyList<int> InitialProcessIds,
    IReadOnlyList<int> GracefullyClosedProcessIds,
    IReadOnlyList<int> ForceTerminatedProcessIds,
    IReadOnlyList<int> RemainingProcessIds)
{
    public bool CommandCompleted { get; init; } = true;
    public bool Success => CommandCompleted && RemainingProcessIds.Count == 0;
}

internal sealed record LunarManagedProcess(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    long MainWindowHandle,
    string MainWindowTitle,
    bool IsMainWindowVisible,
    DateTimeOffset? StartTimeUtc = null);

internal interface ILunarProcessBackend
{
    IReadOnlyList<LunarManagedProcess> Snapshot();
    bool RequestClose(int processId);
    bool ForceTerminate(int processId);
}

public sealed class LunarLaunchLifecycleService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private readonly ILunarProcessBackend _backend;
    private (int Pid, DateTimeOffset Start, string Path)? _controlledRoot;
    private readonly SemaphoreSlim _quitGate = new(1, 1);
    internal int? ControlledRootProcessId => _controlledRoot?.Pid;
    internal (int Pid, DateTimeOffset Start)? ControlledRootIdentity =>
        _controlledRoot is { } root ? (root.Pid, root.Start) : null;

    internal async Task<LunarLauncherCloseResult> CleanupAfterGameAsync(
        string path, TimeSpan timeout, CancellationToken cancellationToken,
        IProcessTreeSnapshot? tree = null, Func<Task<bool>>? sendQuit = null)
    {
        var expectedRoot = _controlledRoot;
        await _quitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_controlledRoot != expectedRoot)
            {
                var remaining = Capture(path).LauncherProcessIds;
                return new(remaining, [], [], remaining) { CommandCompleted = false };
            }
            tree ??= new WindowsProcessTreeSnapshot();
            var deadline = DateTimeOffset.UtcNow + timeout;
            // Lunar may still be disposing a child JVM after its exit has been observed.
            // Wait for the ownership guard; never bypass it for a normal exit or crash.
            while (Capture(path).LauncherRunning && !CanQuitControlledLauncher(path, tree.Capture()) &&
                   DateTimeOffset.UtcNow < deadline)
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            return await QuitControlledLauncherCoreAsync(path, timeout, cancellationToken, tree, sendQuit)
                .ConfigureAwait(false);
        }
        finally { _quitGate.Release(); }
    }

    internal bool RegisterControlledLauncher(string path, int pid)
    {
        var process = _backend.Snapshot().SingleOrDefault(p => p.ProcessId == pid &&
            IsOfficialLauncherProcess(p, path) && p.ExecutablePath is not null);
        if (process?.StartTimeUtc is not { } start) return false;
        _controlledRoot = (pid, start, System.IO.Path.GetFullPath(path));
        return true;
    }

    internal bool CanQuitControlledLauncher(string path, IReadOnlyList<ProcessTreeEntry> tree)
    {
        if (_controlledRoot is not { } root ||
            !string.Equals(root.Path, System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return false;
        var processes = _backend.Snapshot();
        if (processes.Any(IsLikelyMinecraftProcess)) return false;
        var launcher = processes.Where(p => IsOfficialLauncherProcess(p, path)).ToArray();
        if (!launcher.Any(p => p.ProcessId == root.Pid && p.StartTimeUtc == root.Start)) return false;
        // Deeplink targets the singleton, not a PID. Reject other Lunar installations too.
        if (processes.Any(p => Normalize(p.Name) == "lunarclient" && !IsOfficialLauncherProcess(p, path))) return false;
        var owned = new HashSet<int> { root.Pid };
        bool changed;
        do
        {
            changed = false;
            foreach (var p in tree)
                if (owned.Contains(p.ParentProcessId) && p.StartTimeUtc >= root.Start &&
                    p.ExecutablePath is not null && string.Equals(System.IO.Path.GetFullPath(p.ExecutablePath),
                        root.Path, StringComparison.OrdinalIgnoreCase)) changed |= owned.Add(p.ProcessId);
        } while (changed);
        // A JVM without a window can still be starting or shutting down.
        var descendants = new HashSet<int> { root.Pid };
        do
        {
            changed = false;
            foreach (var p in tree)
                if (descendants.Contains(p.ParentProcessId)) changed |= descendants.Add(p.ProcessId);
        } while (changed);
        if (tree.Any(p => descendants.Contains(p.ProcessId) && Normalize(p.Name) is "java" or "javaw")) return false;
        return launcher.All(p => p.StartTimeUtc is not null && owned.Contains(p.ProcessId) &&
            tree.Any(t => t.ProcessId == p.ProcessId && t.StartTimeUtc == p.StartTimeUtc));
    }

    internal async Task<LunarLauncherCloseResult> QuitControlledLauncherAsync(
        string path, TimeSpan timeout, CancellationToken cancellationToken,
        IProcessTreeSnapshot? tree = null, Func<Task<bool>>? sendQuit = null)
    {
        await _quitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await QuitControlledLauncherCoreAsync(path, timeout, cancellationToken, tree, sendQuit).ConfigureAwait(false); }
        finally { _quitGate.Release(); }
    }

    private async Task<LunarLauncherCloseResult> QuitControlledLauncherCoreAsync(
        string path, TimeSpan timeout, CancellationToken cancellationToken,
        IProcessTreeSnapshot? tree, Func<Task<bool>>? sendQuit)
    {
        var initial = Capture(path).LauncherProcessIds.ToArray();
        tree ??= new WindowsProcessTreeSnapshot();
        if (initial.Length == 0) return new(initial, [], [], []);
        if (!CanQuitControlledLauncher(path, tree.Capture())) return new(initial, [], [], initial) { CommandCompleted = false };
        // Revalidate immediately before singleton dispatch. Never fall back to WM_CLOSE or kill.
        if (!CanQuitControlledLauncher(path, tree.Capture())) return new(initial, [], [], initial) { CommandCompleted = false };
        cancellationToken.ThrowIfCancellationRequested();
        var sent = sendQuit is not null ? await sendQuit() : await SendQuitAsync(path, cancellationToken);
        var remaining = await WaitForRemainingAsync(path, timeout, cancellationToken);
        if (remaining.Length == 0) _controlledRoot = null;
        return new(initial, initial.Except(remaining).ToArray(), [], remaining) { CommandCompleted = sent };
    }

    private static async Task<bool> SendQuitAsync(string path, CancellationToken cancellationToken)
    {
        // Lunar's supported singleton IPC sender exits before app/tray initialization.
        var info = OfficialLunarStartInfoFactory.Create(path, true, "lunarclient://close-launcher");
        info.Environment.Remove(NativeBridgeLauncher.BridgeConfigEnvironmentVariable);
        info.Environment.Remove("JAVA_TOOL_OPTIONS");
        LunarLaunchEnvironment.Sanitize(info.Environment);
        using var sender = Process.Start(info) ?? throw new InvalidOperationException("Unable to send Lunar quit command.");
        try
        {
            await sender.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return sender.ExitCode == 0;
        }
        catch (TimeoutException) { return false; }
    }


    public LunarLaunchLifecycleService()
        : this(new WindowsLunarProcessBackend())
    {
    }

    internal LunarLaunchLifecycleService(ILunarProcessBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public LunarPrelaunchSnapshot Capture(string launcherPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        var processes = _backend.Snapshot();
        var launcherIds = processes
            .Where(process => IsOfficialLauncherProcess(process, launcherPath))
            .Select(process => process.ProcessId)
            .Distinct()
            .Order()
            .ToArray();
        var minecraftIds = processes
            .Where(IsLikelyMinecraftProcess)
            .Select(process => process.ProcessId)
            .Distinct()
            .Order()
            .ToArray();
        var visibleLauncherWindows = processes.Count(process =>
            launcherIds.Contains(process.ProcessId) && process.IsMainWindowVisible);
        return new LunarPrelaunchSnapshot(launcherIds, minecraftIds, visibleLauncherWindows);
    }

    public async Task<LunarLauncherCloseResult> CloseLauncherAsync(
        string launcherPath,
        TimeSpan gracefulTimeout,
        TimeSpan forceTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        if (gracefulTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(gracefulTimeout));
        if (forceTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(forceTimeout));

        // These may belong to the user. Never force-kill a pre-existing launcher.
        var initialProcesses = _backend.Snapshot()
            .Where(process => IsOfficialLauncherProcess(process, launcherPath)).ToArray();
        var initial = initialProcesses.Select(process => process.ProcessId).ToArray();
        foreach (var process in initialProcesses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_backend.Snapshot().Any(current => current.ProcessId == process.ProcessId &&
                current.StartTimeUtc == process.StartTimeUtc &&
                IsOfficialLauncherProcess(current, launcherPath)))
                _backend.RequestClose(process.ProcessId);
        }

        var afterGrace = await WaitForRemainingAsync(
            launcherPath, gracefulTimeout, cancellationToken).ConfigureAwait(false);
        var gracefullyClosed = initial.Except(afterGrace).Order().ToArray();
        var remaining = afterGrace;
        int[] forceTerminated = [];

        return new LunarLauncherCloseResult(
            initial,
            gracefullyClosed,
            forceTerminated,
            remaining);
    }

    // Only a process handle created by this launch operation may enter this fallback.
    internal static async Task<bool> CloseOwnedSenderAsync(
        Func<bool> hasExited, Action requestClose, Func<TimeSpan, Task<bool>> waitForExit,
        Action forceTerminate, TimeSpan gracefulTimeout)
    {
        if (hasExited()) return false;
        requestClose();
        if (await waitForExit(gracefulTimeout).ConfigureAwait(false) || hasExited()) return false;
        forceTerminate();
        await waitForExit(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        return true;
    }

    internal static bool IsLikelyMinecraftProcess(LunarManagedProcess process)
    {
        var name = Normalize(process.Name);
        if (name is not ("java" or "javaw"))
            return false;
        if (process.MainWindowHandle == 0 || string.IsNullOrWhiteSpace(process.MainWindowTitle))
            return false;
        return process.MainWindowTitle.Contains("Minecraft", StringComparison.OrdinalIgnoreCase) ||
               process.MainWindowTitle.Contains("Lunar", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsOfficialLauncherProcess(LunarManagedProcess process, string launcherPath)
    {
        if (Normalize(process.Name) != "lunarclient")
            return false;

        if (!string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(process.ExecutablePath),
                    Path.GetFullPath(launcherPath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        return process.MainWindowHandle != 0 &&
               process.MainWindowTitle.Contains("Lunar", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int[]> WaitForRemainingAsync(
        string launcherPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = Capture(launcherPath).LauncherProcessIds.ToArray();
            if (remaining.Length == 0 || DateTimeOffset.UtcNow >= deadline)
                return remaining;
            var delay = deadline - DateTimeOffset.UtcNow;
            if (delay > PollInterval)
                delay = PollInterval;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

internal sealed class WindowsLunarProcessBackend : ILunarProcessBackend
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    public IReadOnlyList<LunarManagedProcess> Snapshot()
    {
        var result = new List<LunarManagedProcess>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                string? path = null;
                long mainWindowHandle = 0;
                string mainWindowTitle = string.Empty;
                try { path = process.MainModule?.FileName; } catch { }
                try
                {
                    mainWindowHandle = process.MainWindowHandle.ToInt64();
                    mainWindowTitle = process.MainWindowTitle ?? string.Empty;
                }
                catch { }
                var visible = mainWindowHandle != 0 && IsWindowVisible(new nint(mainWindowHandle));
                DateTimeOffset? startTime = null;
                try { startTime = process.StartTime.ToUniversalTime(); } catch { }
                result.Add(new LunarManagedProcess(
                    process.Id,
                    process.ProcessName,
                    path,
                    mainWindowHandle,
                    mainWindowTitle,
                    visible, startTime));
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return result;
    }

    private delegate bool EnumWindowCallback(nint window, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder name, int count);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    private static bool IsLauncherWindow(nint window)
    {
        var name = new System.Text.StringBuilder(256);
        return GetClassName(window, name, name.Capacity) > 0 &&
            name.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);
    }

    public bool RequestClose(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return true;
            // CloseMainWindow skips hidden windows, including Moonrise's hidden Electron window.
            var requested = false;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner == (uint)processId && IsLauncherWindow(window))
                    requested |= PostMessage(window, 0x0010 /* WM_CLOSE */, 0, 0);
                return true;
            }, 0);
            return requested;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return true;
        }
    }

    public bool ForceTerminate(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return true;
            // Never terminate the process tree: the Minecraft JVM can be a Lunar child.
            process.Kill(entireProcessTree: false);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return true;
        }
    }
}
