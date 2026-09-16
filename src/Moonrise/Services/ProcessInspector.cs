using System.Diagnostics;
using System.Runtime.InteropServices;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class ProcessInspector
{
    public IReadOnlyDictionary<int, ProcessRecord> Snapshot()
    {
        var result = new Dictionary<int, ProcessRecord>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                string? path = null;
                long mainWindowHandle = 0;
                string mainWindowTitle = string.Empty;
                var isMainWindowVisible = false;
                var isResponding = false;
                try { path = process.MainModule?.FileName; } catch { }
                try
                {
                    var handle = process.MainWindowHandle;
                    mainWindowHandle = handle.ToInt64();
                    mainWindowTitle = process.MainWindowTitle ?? string.Empty;
                    isMainWindowVisible = handle != IntPtr.Zero && IsWindowVisible(handle);
                    isResponding = process.Responding;
                }
                catch { }
                result[process.Id] = new ProcessRecord(
                    process.Id, process.ProcessName, path, mainWindowHandle, mainWindowTitle,
                    isMainWindowVisible, isResponding);
            }
            catch { }
            finally { process.Dispose(); }
        }
        return result;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
}
