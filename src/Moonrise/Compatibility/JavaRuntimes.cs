using System.Collections.Immutable;
using System.Text.RegularExpressions;
namespace Moonrise.Compatibility;

public static class JavaRuntimes
{
    public static JavaCandidate? Inspect(string path)
    {
        var full = Path.GetFullPath(path);
        var executable = Directory.Exists(full)
            ? new[] { Path.Combine(full, "bin", "java.exe"), Path.Combine(full, "bin", "javaw.exe"), Path.Combine(full, "bin", "java") }.FirstOrDefault(File.Exists)
            : File.Exists(full) && new[] { "java", "javaw", "java.exe", "javaw.exe" }.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase) ? full : null;
        if (executable == null) return null;
        var directory = Path.GetDirectoryName(executable)!;
        var root = Path.GetFileName(directory).Equals("bin", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(directory)! : directory;
        var release = Path.Combine(root, "release");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var evidence = ImmutableArray.CreateBuilder<Evidence>();
        evidence.Add(new("filesystem", executable));
        if (File.Exists(release) && new FileInfo(release).Length <= 65536)
        {
            foreach (var line in File.ReadAllLines(release))
            {
                var split = line.IndexOf('=');
                if (split > 0) fields[line[..split]] = line[(split + 1)..].Trim().Trim('"');
            }
            evidence.Add(new("java.release", release));
        }
        fields.TryGetValue("JAVA_VERSION", out var version);
        fields.TryGetValue("IMPLEMENTOR", out var vendor);
        fields.TryGetValue("OS_ARCH", out var arch);
        int? major = null;
        if (version != null)
        {
            var match = Regex.Match(version, @"^(?:1\.)?(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed)) major = parsed;
        }
        return new(executable, root, version, vendor, NormalizeArchitecture(arch), major, evidence.ToImmutable());
    }
    public static ImmutableArray<JavaCandidate> Discover(string lunarRuntimeDirectory)
    {
        if (!Directory.Exists(lunarRuntimeDirectory)) return [];
        // Only runtime release/bin metadata, never accounts, profiles or token files.
        return Directory.EnumerateDirectories(lunarRuntimeDirectory).OrderBy(p => p, StringComparer.Ordinal).Take(128)
            .Prepend(lunarRuntimeDirectory).Select(Inspect).OfType<JavaCandidate>().ToImmutableArray();
    }
    public static JavaResolution Resolve(JavaRuntimeMode mode, string? custom, ImmutableArray<JavaCandidate> candidates, JavaRequirement requirement)
    {
        var issues = ImmutableArray.CreateBuilder<Issue>();
        JavaCandidate? selected;
        if (mode == JavaRuntimeMode.Custom)
        {
            try { selected = string.IsNullOrWhiteSpace(custom) ? null : Inspect(custom); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { selected = null; }
            if (selected == null) issues.Add(new("java.custom.missing", Severity.Error, "Custom Java path is absent or does not resolve to java/javaw.", custom));
        }
        else
        {
            selected = candidates.Where(c => !Incompatible(c, requirement)).OrderBy(c => Unverified(c, requirement)).ThenBy(c => c.Major == null)
                .ThenBy(c => c.Major).ThenBy(c => c.Version, StringComparer.Ordinal).ThenBy(c => c.Executable, StringComparer.Ordinal).FirstOrDefault();
            if (selected == null) issues.Add(new("java.auto.unavailable", Severity.Error, "No available Java runtime satisfies the contract/package requirements."));
        }
        if (selected != null)
        {
            if (Incompatible(selected, requirement)) issues.Add(new("java.incompatible", Severity.Error, "Java version/architecture conflicts with requirements.", selected.Executable, selected.Evidence));
            if (selected.Major == null || selected.Architecture == null)
                issues.Add(new("java.metadata.unknown", Unverified(selected, requirement) ? Severity.Error : Severity.Warning,
                    "Java metadata is incomplete; compatibility cannot be verified.", selected.Executable));
            if (!File.Exists(selected.Executable)) issues.Add(new("java.executable.missing", Severity.Error, "Resolved Java executable is missing.", selected.Executable));
        }
        var status = issues.Any(i => i.Severity == Severity.Error) ? PreflightStatus.Invalid : issues.Count > 0 ? PreflightStatus.ValidWithWarnings : PreflightStatus.Valid;
        return new(mode, selected == null ? ResolutionState.Unknown : ResolutionState.Resolved, selected, status, issues.ToImmutable());
    }
    private static bool Unverified(JavaCandidate c, JavaRequirement r) =>
        c.Major == null && (r.Minimum != null || r.Maximum != null) || c.Architecture == null && r.Architecture != null;
    private static bool Incompatible(JavaCandidate c, JavaRequirement r) =>
        c.Major != null && (r.Minimum != null && c.Major < r.Minimum || r.Maximum != null && c.Major > r.Maximum) ||
        c.Architecture != null && r.Architecture != null && c.Architecture != NormalizeArchitecture(r.Architecture);
    private static string? NormalizeArchitecture(string? value) => value?.ToLowerInvariant() switch { "amd64" or "x86_64" => "x64", "i386" or "i686" => "x86", "aarch64" => "arm64", var other => other };
}

/// <summary>A deliberately conservative version-range interpreter; unsupported syntax is unknown.</summary>
public static class VersionRanges
{
    public static bool? Matches(string version, string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == "*") return true;
        if (range.StartsWith('[') || range.StartsWith('('))
        {
            var match = Regex.Match(range, @"^([\[(])([^,]*),([^\])]*)([\])])$");
            if (!match.Success) return range == "[" + version + "]" ? true : null;
            var lower = match.Groups[2].Value.Trim(); var upper = match.Groups[3].Value.Trim();
            var low = lower.Length == 0 ? 1 : Compare(version, lower); var high = upper.Length == 0 ? -1 : Compare(version, upper);
            if (low == null || high == null) return null;
            return (low > 0 || low == 0 && match.Groups[1].Value == "[") && (high < 0 || high == 0 && match.Groups[4].Value == "]");
        }
        var parts = range.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool result = true;
        foreach (var part in parts)
        {
            var match = Regex.Match(part, @"^(>=|<=|>|<|=)?(\d+(?:\.\d+)*(?:\.\*)?)$");
            if (!match.Success) return null;
            var target = match.Groups[2].Value;
            if (target.EndsWith(".*", StringComparison.Ordinal)) { result &= version.StartsWith(target[..^1], StringComparison.Ordinal); continue; }
            var comparison = Compare(version, target); if (comparison == null) return null;
            result &= match.Groups[1].Value switch { ">=" => comparison >= 0, "<=" => comparison <= 0, ">" => comparison > 0, "<" => comparison < 0, _ => comparison == 0 };
        }
        return result;
    }
    private static int? Compare(string left, string right)
    {
        if (!Regex.IsMatch(left, @"^\d+(\.\d+)*$") || !Regex.IsMatch(right, @"^\d+(\.\d+)*$")) return null;
        var a = left.Split('.'); var b = right.Split('.');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            if (!int.TryParse(i < a.Length ? a[i] : "0", out var av) || !int.TryParse(i < b.Length ? b[i] : "0", out var bv)) return null;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }
}
