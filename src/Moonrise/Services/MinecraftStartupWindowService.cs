using System.Runtime.InteropServices;
using System.Text;
using Moonrise.Models;

namespace Moonrise.Services;

internal interface IMinecraftStartupWindowOperations
{
    string GetClassName(nint handle);
    bool Hide(nint handle);
    void Restore(nint handle);
}

public sealed class MinecraftStartupWindowService
{
    private static readonly TimeSpan MaximumHiddenDuration = TimeSpan.FromSeconds(15);
    private readonly IMinecraftStartupWindowOperations _windows;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<string>? _audit;
    private readonly Dictionary<int, StagedWindow> _staged = [];

    public MinecraftStartupWindowService(Action<string>? audit = null)
        : this(new WindowsMinecraftStartupWindowOperations(), () => DateTimeOffset.UtcNow, audit)
    {
    }

    internal MinecraftStartupWindowService(
        IMinecraftStartupWindowOperations windows,
        Func<DateTimeOffset> utcNow,
        Action<string>? audit = null)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _audit = audit;
    }

    public void Observe(IEnumerable<ProcessRecord> javaProcesses)
    {
        ArgumentNullException.ThrowIfNull(javaProcesses);
        var processes = javaProcesses.ToDictionary(item => item.ProcessId);
        foreach (var staged in _staged.ToArray())
        {
            if (!processes.TryGetValue(staged.Key, out var process))
            {
                _staged.Remove(staged.Key);
                continue;
            }

            var timedOut = _utcNow() - staged.Value.HiddenUtc >= MaximumHiddenDuration;
            if ((!string.IsNullOrWhiteSpace(process.MainWindowTitle) && process.IsResponding) || timedOut)
            {
                _windows.Restore(staged.Value.Handle);
                _staged.Remove(staged.Key);
                Audit($"Minecraft startup window restored: pid={staged.Key}; reason={(timedOut ? "timeout" : "ready")}");
            }
        }

        foreach (var process in processes.Values)
        {
            if (_staged.ContainsKey(process.ProcessId) ||
                process.MainWindowHandle == 0 ||
                !process.IsMainWindowVisible ||
                !string.IsNullOrWhiteSpace(process.MainWindowTitle))
            {
                continue;
            }

            var handle = new nint(process.MainWindowHandle);
            if (!string.Equals(_windows.GetClassName(handle), "LWJGL", StringComparison.OrdinalIgnoreCase) ||
                !_windows.Hide(handle))
            {
                continue;
            }

            _staged[process.ProcessId] = new StagedWindow(handle, _utcNow());
            Audit($"Blank Minecraft startup window hidden: pid={process.ProcessId}");
        }
    }

    public void RestoreAll()
    {
        foreach (var staged in _staged.Values)
            _windows.Restore(staged.Handle);
        _staged.Clear();
    }

    private void Audit(string message)
    {
        try { _audit?.Invoke(message); }
        catch { }
    }

    private sealed record StagedWindow(nint Handle, DateTimeOffset HiddenUtc);
}

internal sealed class WindowsMinecraftStartupWindowOperations : IMinecraftStartupWindowOperations
{
    private const int SwHide = 0;
    private const int SwRestore = 9;

    public string GetClassName(nint handle)
    {
        var builder = new StringBuilder(128);
        return GetClassNameNative(handle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    public bool Hide(nint handle) => ShowWindowAsync(handle, SwHide);

    public void Restore(nint handle) => _ = ShowWindowAsync(handle, SwRestore);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassNameNative(nint handle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint handle, int command);
}
