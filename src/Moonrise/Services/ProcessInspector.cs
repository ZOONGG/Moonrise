using System.Diagnostics;
using System.Runtime.InteropServices;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class ProcessInspector
{
    public IReadOnlyDictionary<int, ProcessRecord> Snapshot(bool minecraftWindowsOnly = false)
    {
        var result = new Dictionary<int, ProcessRecord>();
        var windows = new WindowsMinecraftStartupWindowOperations();
        foreach (var entry in new WindowsProcessTreeSnapshot().Capture(includeExecutablePaths: !minecraftWindowsOnly))
        {
            if (minecraftWindowsOnly &&
                !entry.Name.Equals("java", StringComparison.OrdinalIgnoreCase) &&
                !entry.Name.Equals("javaw", StringComparison.OrdinalIgnoreCase))
            {
                result[entry.ProcessId] = new ProcessRecord(entry.ProcessId, entry.Name,
                    null, 0, "", false, false, entry.ParentProcessId, entry.StartTimeUtc, "");
                continue;
            }
            Process? process = null;
            try
            {
                process = Process.GetProcessById(entry.ProcessId);
                string? path = null;
                long mainWindowHandle = 0;
                string mainWindowTitle = string.Empty;
                var isMainWindowVisible = false;
                var isResponding = false;
                if (!minecraftWindowsOnly)
                    try { path = process.MainModule?.FileName; } catch { }
                try
                {
                    var handle = process.MainWindowHandle;
                    mainWindowHandle = handle.ToInt64();
                    mainWindowTitle = process.MainWindowTitle ?? string.Empty;
                    isMainWindowVisible = handle != IntPtr.Zero && IsWindowVisible(handle);
                    isResponding = minecraftWindowsOnly
                        ? handle != IntPtr.Zero && IsWindowResponding(handle)
                        : process.Responding;
                }
                catch { }
                result[process.Id] = new ProcessRecord(
                    process.Id, process.ProcessName, path, mainWindowHandle, mainWindowTitle,
                    isMainWindowVisible, isResponding, entry.ParentProcessId, entry.StartTimeUtc,
                    mainWindowHandle == 0 ? "" : windows.GetClassName(new nint(mainWindowHandle)));
            }
            catch { }
            finally { process?.Dispose(); }
        }
        return result;
    }

    // WM_NULL checks the actual window, with a bounded wait even during JVM startup.
    internal static bool IsWindowResponding(nint handle) =>
        SendMessageTimeout(handle, 0, 0, 0, 0x0002 | 0x0020, 100, out _) != 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint handle, uint message, nuint wParam,
        nint lParam, uint flags, uint timeout, out nuint result);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
}
