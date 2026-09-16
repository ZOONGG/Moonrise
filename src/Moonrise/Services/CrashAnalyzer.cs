using System.Text;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class CrashAnalyzer
{
    public CrashAnalysisResult Analyze(
        int processId,
        int? exitCode,
        IEnumerable<string> managedDiagnostics,
        string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);
        var diagnostics = managedDiagnostics.Select(TokenRedactor.Redact).TakeLast(100).ToArray();
        var joined = string.Join('\n', diagnostics);
        var (category, summary, recommendations) = Classify(exitCode, joined);
        var path = Path.Combine(reportDirectory, $"moonrise-crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{processId}.txt");
        var report = new StringBuilder()
            .AppendLine("Moonrise crash analysis")
            .AppendLine($"Time: {DateTimeOffset.Now:O}")
            .AppendLine($"Process: {processId}")
            .AppendLine($"Exit code: {exitCode?.ToString() ?? "unknown"}")
            .AppendLine($"Category: {category}")
            .AppendLine($"Summary: {summary}")
            .AppendLine()
            .AppendLine("Recommendations:");
        foreach (var recommendation in recommendations) report.AppendLine($"- {recommendation}");
        report.AppendLine().AppendLine("Moonrise managed diagnostics (redacted):");
        foreach (var line in diagnostics) report.AppendLine(line);
        File.WriteAllText(path, TokenRedactor.Redact(report.ToString()), new UTF8Encoding(false));
        return new CrashAnalysisResult(category, summary, recommendations, path);
    }

    private static (string Category, string Summary, IReadOnlyList<string> Recommendations) Classify(
        int? exitCode,
        string diagnostics)
    {
        if (diagnostics.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase))
            return ("memory", "The game appears to have exhausted available Java memory.",
                ["Reduce the enabled package set.", "Review the memory allocation in the official launcher."]);
        if (diagnostics.Contains("mixin", StringComparison.OrdinalIgnoreCase) ||
            diagnostics.Contains("NoClassDefFoundError", StringComparison.OrdinalIgnoreCase) ||
            diagnostics.Contains("ClassNotFoundException", StringComparison.OrdinalIgnoreCase))
            return ("package-compatibility", "A package or dependency may be incompatible with this client version.",
                ["Run once in safe mode.", "Check package versions and declared dependencies."]);
        if (exitCode is -1073741819 or -1073740791)
            return ("native", "The process ended with a native Windows fault.",
                ["Update graphics drivers.", "Run once in safe mode to exclude third-party packages."]);
        return ("unknown", "The process ended unexpectedly and no known signature was found.",
            ["Run once in safe mode.", "Review this local report before sharing a sanitized copy."]);
    }
}
