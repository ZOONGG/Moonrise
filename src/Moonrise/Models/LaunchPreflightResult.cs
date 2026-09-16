namespace Moonrise.Models;

public sealed record LaunchPreflightResult(
    IReadOnlyList<PackageInfo> Mods,
    IReadOnlyList<PackageInfo> Agents,
    CompatibilityReport Compatibility,
    bool SafeMode);

public sealed record CrashAnalysisResult(
    string Category,
    string Summary,
    IReadOnlyList<string> Recommendations,
    string ReportPath);
