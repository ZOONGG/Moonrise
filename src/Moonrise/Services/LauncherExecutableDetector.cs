using System.Diagnostics;
using Microsoft.Win32;

namespace Moonrise.Services;

public sealed class LauncherExecutableDetector
{
    public IReadOnlyList<string> Detect()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var root in new[] { local, programFiles }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            candidates.Add(Path.Combine(root, "Programs", "Lunar Client", "Lunar Client.exe"));
            candidates.Add(Path.Combine(root, "Programs", "lunarclient", "Lunar Client.exe"));
            candidates.Add(Path.Combine(root, "Lunar Client", "Lunar Client.exe"));
        }
        AddRegistryLocations(candidates);
        return candidates.Where(IsSupportedLauncher).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<Process> GetRunningLaunchers()
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id != Environment.ProcessId && Normalize(process.ProcessName) == "lunarclient") result.Add(process);
                else process.Dispose();
            }
            catch { process.Dispose(); }
        }
        return result;
    }

    public static bool IsSupportedLauncher(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
        string.Equals(Path.GetFileName(path), "Lunar Client.exe", StringComparison.OrdinalIgnoreCase);

    private static void AddRegistryLocations(ISet<string> candidates)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        using var entry = uninstall.OpenSubKey(name);
                        if ((entry?.GetValue("DisplayName") as string)?.Contains("Lunar Client", StringComparison.OrdinalIgnoreCase) != true) continue;
                        var location = entry?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(location)) candidates.Add(Path.Combine(location, "Lunar Client.exe"));
                    }
                }
                catch { }
            }
    }

    private static string Normalize(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
