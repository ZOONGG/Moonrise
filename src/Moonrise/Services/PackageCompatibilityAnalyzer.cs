using Moonrise.Models;

namespace Moonrise.Services;

public sealed class PackageCompatibilityAnalyzer
{
    public CompatibilityReport Analyze(string gameVersion, IEnumerable<PackageInfo> packages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        var enabled = packages.Where(package => package.IsEnabled).ToArray();
        var issues = new List<CompatibilityIssue>();
        var byId = enabled.GroupBy(package => package.Identifier, StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var duplicate in byId.Where(group => group.Count() > 1))
        {
            foreach (var package in duplicate)
                issues.Add(new CompatibilityIssue(CompatibilitySeverity.Error, "duplicate-id", package.DisplayName,
                    $"Multiple enabled packages use the identifier '{duplicate.Key}'."));
        }

        var identifiers = enabled.Select(package => package.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var package in enabled)
        {
            if (package.CompatibleGameVersions.Count == 0)
            {
                issues.Add(new CompatibilityIssue(CompatibilitySeverity.Warning, "version-unknown", package.DisplayName,
                    "The package does not declare compatible Minecraft versions."));
            }
            else if (!package.CompatibleGameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new CompatibilityIssue(CompatibilitySeverity.Error, "version-incompatible", package.DisplayName,
                    $"The package does not declare support for Minecraft {gameVersion}."));
            }

            foreach (var conflict in package.Conflicts.Where(identifiers.Contains))
                issues.Add(new CompatibilityIssue(CompatibilitySeverity.Error, "declared-conflict", package.DisplayName,
                    $"The package declares a conflict with '{conflict}'."));

            foreach (var requirement in package.Requires.Where(required => !identifiers.Contains(required)))
                issues.Add(new CompatibilityIssue(CompatibilitySeverity.Error, "missing-requirement", package.DisplayName,
                    $"Required package '{requirement}' is not enabled."));
        }

        return new CompatibilityReport(issues);
    }
}
