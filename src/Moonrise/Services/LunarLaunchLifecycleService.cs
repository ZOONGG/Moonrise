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
    public bool Success => RemainingProcessIds.Count == 0;
}

internal sealed record LunarManagedProcess(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    long MainWindowHandle,
    string MainWindowTitle,
    bool IsMainWindowVisible);

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

        var initial = Capture(launcherPath).LauncherProcessIds.ToArray();
        foreach (var processId in initial)
            _backend.RequestClose(processId);

        var afterGrace = await WaitForRemainingAsync(
            launcherPath,
            gracefulTimeout,
            cancellationToken).ConfigureAwait(false);
        var gracefullyClosed = initial.Except(afterGrace).Order().ToArray();

        foreach (var processId in afterGrace)
            _backend.ForceTerminate(processId);

        var remaining = await WaitForRemainingAsync(
            launcherPath,
            forceTimeout,
            cancellationToken).ConfigureAwait(false);
        var forceTerminated = afterGrace.Except(remaining).Order().ToArray();

        return new LunarLauncherCloseResult(
            initial,
            gracefullyClosed,
            forceTerminated,
            remaining);
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
                result.Add(new LunarManagedProcess(
                    process.Id,
                    process.ProcessName,
                    path,
                    mainWindowHandle,
                    mainWindowTitle,
                    visible));
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

    public bool RequestClose(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return true;
            return process.CloseMainWindow();
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
