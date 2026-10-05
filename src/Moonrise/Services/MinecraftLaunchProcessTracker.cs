using Moonrise.Models;

namespace Moonrise.Services;

// Monitoring ancestry is separate from Lunar window suppression ownership.
// Retain observed ancestors across bootstrap exit, but never adopt a reused PID.
internal sealed class MinecraftLaunchProcessTracker
{
    private readonly Dictionary<int, DateTimeOffset> _ancestors = [];
    private readonly HashSet<int> _reusedIds = [];
    private readonly HashSet<(int, DateTimeOffset?)> _minecraft = [];
    private readonly Dictionary<(int, DateTimeOffset?), int> _visibleSamples = [];
    private DateTimeOffset? _gameMissingSince, _launcherMissingSince;

    public bool SawMinecraft => _minecraft.Count > 0;
    public bool SawJava { get; private set; }

    public string? FailureStage(DateTimeOffset now, DateTimeOffset deadline,
        IReadOnlyList<ProcessRecord> candidates, bool? launcherRunning)
    {
        if (now >= deadline) return "minecraft-timeout";
        var gameRunning = candidates.Any(IsMinecraft);
        if (SawMinecraft && !gameRunning)
        {
            _gameMissingSince ??= now;
            if (now - _gameMissingSince >= TimeSpan.FromSeconds(5))
                return "java-exited-before-usable-window";
        }
        else _gameMissingSince = null;
        // An observed bootstrap can hand off after both it and the launcher exit.
        // Keep the existing launch deadline; never extend it on process replacement.
        if (!SawJava && candidates.Count == 0 && launcherRunning == false)
        {
            _launcherMissingSince ??= now;
            if (now - _launcherMissingSince >= TimeSpan.FromSeconds(5))
                return "lunar-exited-before-java";
        }
        else _launcherMissingSince = null;
        return null;
    }

    public ProcessRecord? ObserveUsableWindow(IReadOnlyList<ProcessRecord> candidates)
    {
        var live = candidates.Select(p => (p.ProcessId, p.StartTimeUtc)).ToHashSet();
        foreach (var key in _visibleSamples.Keys.Where(key => !live.Contains(key)).ToArray())
            _visibleSamples.Remove(key);
        foreach (var candidate in candidates)
        {
            var key = (candidate.ProcessId, candidate.StartTimeUtc);
            if (!IsMinecraft(candidate) || candidate.MainWindowHandle == 0 ||
                !candidate.IsMainWindowVisible || !candidate.IsResponding ||
                string.IsNullOrWhiteSpace(candidate.MainWindowTitle))
            {
                _visibleSamples.Remove(key);
                continue;
            }
            _visibleSamples[key] = _visibleSamples.GetValueOrDefault(key) + 1;
            if (_visibleSamples[key] >= 6) return candidate;
        }
        return null;
    }

    public ProcessRecord[] Observe(IReadOnlyDictionary<int, ProcessRecord> snapshot,
        IEnumerable<int> verifiedLauncherIds, IReadOnlySet<int> initialProcessIds)
    {
        foreach (var (id, start) in _ancestors.ToArray())
            if (snapshot.TryGetValue(id, out var current) && current.StartTimeUtc != start)
            {
                _ancestors.Remove(id);
                _reusedIds.Add(id);
            }
        foreach (var id in verifiedLauncherIds)
            if (!_reusedIds.Contains(id) && snapshot.TryGetValue(id, out var root) && root.StartTimeUtc is { } start)
                _ancestors.TryAdd(id, start);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var process in snapshot.Values)
            {
                if (initialProcessIds.Contains(process.ProcessId) ||
                    _reusedIds.Contains(process.ProcessId) ||
                    _ancestors.ContainsKey(process.ProcessId) ||
                    process.StartTimeUtc is not { } start ||
                    !_ancestors.TryGetValue(process.ParentProcessId, out var parentStart) ||
                    start < parentStart) continue;
                if (snapshot.TryGetValue(process.ParentProcessId, out var parent) &&
                    parent.StartTimeUtc != parentStart) continue;
                _ancestors.Add(process.ProcessId, start);
                changed = true;
            }
        }

        var candidates = snapshot.Values.Where(process =>
            !initialProcessIds.Contains(process.ProcessId) &&
            (process.Name.Equals("java", StringComparison.OrdinalIgnoreCase) ||
             process.Name.Equals("javaw", StringComparison.OrdinalIgnoreCase)) &&
            _ancestors.TryGetValue(process.ProcessId, out var start) &&
            process.StartTimeUtc == start).ToArray();
        SawJava |= candidates.Length > 0;
        foreach (var candidate in candidates)
            if (candidate.MainWindowClass.Equals("LWJGL", StringComparison.OrdinalIgnoreCase) ||
                candidate.MainWindowClass.Equals("GLFW30", StringComparison.OrdinalIgnoreCase))
                _minecraft.Add((candidate.ProcessId, candidate.StartTimeUtc));
        return candidates;
    }

    public bool IsMinecraft(ProcessRecord process) =>
        _minecraft.Contains((process.ProcessId, process.StartTimeUtc));

    public Task<int?>? ObserveGameExit(ProcessRecord process, Func<int, Task<int?>> observeExit) =>
        IsMinecraft(process) ? observeExit(process.ProcessId) : null;
}
