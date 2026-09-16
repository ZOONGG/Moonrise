namespace Moonrise.Models;

public enum CompatibilitySeverity
{
    Information,
    Warning,
    Error
}

public sealed record CompatibilityIssue(
    CompatibilitySeverity Severity,
    string Code,
    string PackageName,
    string Detail);

public sealed record CompatibilityReport(IReadOnlyList<CompatibilityIssue> Issues)
{
    public bool CanLaunch => Issues.All(issue => issue.Severity != CompatibilitySeverity.Error);
    public int ErrorCount => Issues.Count(issue => issue.Severity == CompatibilitySeverity.Error);
    public int WarningCount => Issues.Count(issue => issue.Severity == CompatibilitySeverity.Warning);
}
