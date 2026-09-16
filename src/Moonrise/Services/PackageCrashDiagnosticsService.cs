using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed record PackageCrashCaptureRequest(
    DateTimeOffset LaunchStartedUtc,
    DateTimeOffset FailureUtc,
    string FailureStage,
    int? ProcessExitCode,
    IReadOnlyList<PackageInfo> EnabledPackages,
    string? WeaveLoaderPath,
    string? WeaveLoaderSha256,
    string LaunchReportPath,
    string CleanupResult,
    IReadOnlyList<string> MoonriseDiagnostics,
    IReadOnlyList<string>? AdditionalLogRoots = null,
    string? JavaOutputPath = null,
    string MinecraftVersion = "1.8.9",
    string LanguageCode = "en",
    string? WeaveLoaderVersion = null);

public sealed record PackageCrashBundleResult(
    string DirectoryPath,
    string SummaryPath,
    string ReportPath,
    string? LunarCrashIdentifier,
    long TotalBytes);

public sealed partial class PackageCrashDiagnosticsService
{
    internal const int MaximumCapturedTextBytes = 128 * 1024;
    internal const long MaximumBundleBytes = 1024L * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PackageCrashBundleResult Capture(PackageCrashCaptureRequest request, string crashesRoot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(crashesRoot);
        Directory.CreateDirectory(crashesRoot);
        var directory = CreateBundleDirectory(crashesRoot, request.FailureUtc);
        var writtenTextBytes = 0L;

        var candidates = FindCurrentLaunchLogs(request)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        string? crashIdentifier = null;
        foreach (var candidate in candidates
                     .Where(file => ContainsAny(file.FullName, "lunar", "launcher", "main.log"))
                     .Take(50))
        {
            try
            {
                crashIdentifier ??= FindCrashIdentifier(
                    SanitizeCapturedText(ReadTail(candidate.FullName, 32 * 1024)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        var captured = new List<(string Name, FileInfo Source, string Text)>();
        CaptureCategory(captured, candidates, "recent-weave-log.txt", file => ContainsAny(file, "weave"));
        CaptureCategory(captured, candidates, "recent-mixin-log.txt", file => ContainsAny(file, "mixin"));
        CaptureCategory(captured, candidates, "recent-lunar-log.txt", file =>
            ContainsAny(file, "lunar", "launcher", "main.log"));
        CaptureCategory(captured, candidates, "recent-java-output.txt", file =>
            ContainsAny(file, "java", "stdout", "stderr", "latest.log"));
        if (!string.IsNullOrWhiteSpace(request.JavaOutputPath) &&
            File.Exists(request.JavaOutputPath) &&
            IsInLaunchWindow(new FileInfo(request.JavaOutputPath), request))
        {
            CaptureExplicit(captured, "recent-java-output.txt", new FileInfo(request.JavaOutputPath));
        }

        foreach (var (name, _, text) in captured
                     .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var bounded = BoundText(SanitizeCapturedText(text), MaximumCapturedTextBytes);
            WriteSanitizedText(Path.Combine(directory, name), bounded);
            writtenTextBytes += Encoding.UTF8.GetByteCount(bounded);
            crashIdentifier ??= FindCrashIdentifier(bounded);
        }

        var packages = request.EnabledPackages.Select(package => new
        {
            package.PackageId,
            FileName = package.OriginalFileName,
            package.Sha256,
            Kind = package.Kind.ToString()
        }).ToArray();
        WriteSanitizedJson(Path.Combine(directory, "enabled-packages.json"), packages);

        var relevantPaths = captured
            .Select(item => SanitizePath(item.Source.FullName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        WriteSanitizedText(
            Path.Combine(directory, "relevant-log-paths.txt"),
            relevantPaths.Length == 0
                ? "No launch-window log files were found." + Environment.NewLine
                : string.Join(Environment.NewLine, relevantPaths) + Environment.NewLine);

        var launchReportDestination = Path.Combine(directory, "moonrise-launch-report.json");
        if (File.Exists(request.LaunchReportPath))
        {
            var launchReport = ReadTail(request.LaunchReportPath, MaximumCapturedTextBytes);
            WriteSanitizedText(launchReportDestination, launchReport);
        }
        else
        {
            WriteSanitizedText(launchReportDestination, "{\"status\":\"unavailable\"}" + Environment.NewLine);
        }

        var diagnostics = BoundText(
            string.Join(Environment.NewLine, request.MoonriseDiagnostics.TakeLast(200)),
            MaximumCapturedTextBytes);
        WriteSanitizedText(Path.Combine(directory, "moonrise-diagnostics.txt"), diagnostics);

        var summary = new
        {
            CreatedUtc = request.FailureUtc.ToUniversalTime().ToString("O"),
            request.FailureStage,
            request.ProcessExitCode,
            EnabledPackageCount = packages.Length,
            WeaveLoader = string.IsNullOrWhiteSpace(request.WeaveLoaderPath)
                ? null
                : new
                {
                    Path = SanitizePath(request.WeaveLoaderPath),
                    Version = request.WeaveLoaderVersion ?? "unknown",
                    Sha256 = request.WeaveLoaderSha256
                },
            LunarCrashIdentifier = crashIdentifier,
            request.CleanupResult,
            LaunchReport = "moonrise-launch-report.json",
            CapturedTextBytes = writtenTextBytes,
            SizeLimitBytes = MaximumBundleBytes
        };
        var summaryPath = Path.Combine(directory, "crash-summary.json");
        WriteSanitizedJson(summaryPath, summary);
        var reportPath = Path.Combine(directory, "report.txt");
        WriteSanitizedText(
            reportPath,
            CreateHumanReadableReport(request, directory, crashIdentifier, captured));

        EnforceBundleLimit(directory, [
            "crash-summary.json",
            "report.txt",
            "enabled-packages.json",
            "relevant-log-paths.txt",
            "moonrise-launch-report.json",
            "moonrise-diagnostics.txt"
        ]);
        var total = Directory.EnumerateFiles(directory)
            .Select(path => new FileInfo(path).Length)
            .Sum();
        return new PackageCrashBundleResult(directory, summaryPath, reportPath, crashIdentifier, total);
    }

    private static string CreateBundleDirectory(string root, DateTimeOffset timestamp)
    {
        var prefix = timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss");
        for (var index = 0; index < 1000; index++)
        {
            var candidate = Path.Combine(root, index == 0 ? prefix : $"{prefix}-{index:000}");
            if (Directory.Exists(candidate))
                continue;
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        throw new IOException("Unable to allocate a unique crash bundle directory.");
    }

    private static string CreateHumanReadableReport(
        PackageCrashCaptureRequest request,
        string directory,
        string? crashIdentifier,
        IReadOnlyCollection<(string Name, FileInfo Source, string Text)> captured)
    {
        var russian = request.LanguageCode.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        builder.AppendLine(russian ? "Отчёт о неудачном запуске Moonrise" : "Moonrise failed launch report");
        builder.AppendLine(new string('=', 42));
        builder.AppendLine($"{(russian ? "Дата и время" : "Date and time")}: {request.FailureUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"{(russian ? "Версия Minecraft" : "Minecraft version")}: {request.MinecraftVersion}");
        builder.AppendLine($"{(russian ? "Этап сбоя" : "Failure stage")}: {request.FailureStage}");
        builder.AppendLine($"{(russian ? "Код завершения Java" : "Java exit code")}: {request.ProcessExitCode?.ToString() ?? (russian ? "неизвестен" : "unknown")}");
        builder.AppendLine($"{(russian ? "Результат очистки" : "Cleanup result")}: {request.CleanupResult}");
        builder.AppendLine($"{(russian ? "Идентификатор сбоя Lunar" : "Lunar crash identifier")}: {crashIdentifier ?? (russian ? "не найден" : "not found")}");
        builder.AppendLine($"{(russian ? "Вероятная общая категория" : "Likely generic failure category")}: {GenericFailureCategory(request.FailureStage, russian)}");
        builder.AppendLine();
        builder.AppendLine(russian ? "Включённые пакеты:" : "Enabled packages:");
        if (request.EnabledPackages.Count == 0)
        {
            builder.AppendLine(russian ? "- Нет" : "- None");
        }
        else
        {
            foreach (var package in request.EnabledPackages)
            {
                var kind = package.Kind switch
                {
                    PackageKind.WeaveMod => russian ? "Weave-мод" : "Weave mod",
                    PackageKind.JavaAgent => russian ? "Java-агент" : "Java agent",
                    _ => russian ? "без типа" : "unclassified"
                };
                builder.AppendLine($"- {package.DisplayName} ({kind})");
                builder.AppendLine($"  SHA-256: {package.Sha256}");
            }
        }
        builder.AppendLine();
        builder.AppendLine(russian ? "Weave Loader:" : "Weave Loader:");
        builder.AppendLine($"- {(russian ? "Версия" : "Version")}: {request.WeaveLoaderVersion ?? (russian ? "неизвестна" : "unknown")}");
        builder.AppendLine($"- SHA-256: {request.WeaveLoaderSha256 ?? (russian ? "не используется" : "not used")}");
        builder.AppendLine();
        builder.AppendLine(russian ? "Технические файлы в этом пакете:" : "Technical files in this bundle:");
        var technicalNames = new[]
            {
                "crash-summary.json",
                "enabled-packages.json",
                "relevant-log-paths.txt",
                "moonrise-launch-report.json",
                "moonrise-diagnostics.txt"
            }
            .Concat(captured.Select(item => item.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in technicalNames)
            builder.AppendLine($"- {SanitizePath(Path.Combine(directory, name))}");
        builder.AppendLine();
        builder.AppendLine(russian
            ? "Совместимость включённых пакетов с текущей средой пока не подтверждена."
            : "Compatibility of the enabled packages with the current environment is not yet confirmed.");
        return builder.ToString();
    }

    private static string GenericFailureCategory(string failureStage, bool russian)
    {
        if (failureStage.Contains("java-exited", StringComparison.OrdinalIgnoreCase))
            return russian
                ? "Процесс Java завершился во время запуска или загрузки пакетов"
                : "Java exited during launch or package loading";
        if (failureStage.Contains("lunar-exited", StringComparison.OrdinalIgnoreCase))
            return russian
                ? "Официальный Lunar завершился до запуска Minecraft"
                : "Official Lunar exited before Minecraft started";
        if (failureStage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return russian
                ? "Истекло время ожидания запуска"
                : "Launch wait timed out";
        return russian ? "Общий сбой запуска" : "Generic launch failure";
    }

    private static IReadOnlyList<FileInfo> FindCurrentLaunchLogs(PackageCrashCaptureRequest request)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(userProfile, ".lunarclient", "logs"),
            Path.Combine(userProfile, ".weave", "logs")
        };
        if (!string.IsNullOrWhiteSpace(request.MinecraftVersion))
        {
            var version = request.MinecraftVersion.Trim();
            roots.Add(Path.Combine(userProfile, ".lunarclient", "profiles", version, "logs"));
            var components = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (components.Length >= 2)
            {
                var profileFamily = $"{components[0]}.{components[1]}";
                roots.Add(Path.Combine(
                    userProfile,
                    ".lunarclient",
                    "profiles",
                    profileFamily,
                    "logs"));
            }
        }
        if (request.AdditionalLogRoots is not null)
        {
            foreach (var root in request.AdditionalLogRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
                roots.Add(Path.GetFullPath(root));
        }

        var files = new List<FileInfo>();
        foreach (var root in roots.Where(Directory.Exists))
            EnumerateSafeLogFiles(root, files, depth: 0);
        return files.Where(file => IsInLaunchWindow(file, request)).ToArray();
    }

    private static void EnumerateSafeLogFiles(string directory, ICollection<FileInfo> result, int depth)
    {
        if (depth > 6 || IsSensitivePath(directory))
            return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                if (IsSensitivePath(path))
                    continue;
                var extension = Path.GetExtension(path);
                if (extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".out", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new FileInfo(path));
                }
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
                EnumerateSafeLogFiles(child, result, depth + 1);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private static bool IsSensitivePath(string path)
    {
        var name = Path.GetFileName(path);
        return ContainsAny(name, "account", "credential", "password", "token", "auth");
    }

    private static bool IsInLaunchWindow(FileInfo file, PackageCrashCaptureRequest request)
    {
        try
        {
            var changed = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            return changed >= request.LaunchStartedUtc.ToUniversalTime().AddSeconds(-2) &&
                changed <= request.FailureUtc.ToUniversalTime().AddMinutes(1);
        }
        catch
        {
            return false;
        }
    }

    private static void CaptureCategory(
        ICollection<(string Name, FileInfo Source, string Text)> captured,
        IEnumerable<FileInfo> candidates,
        string outputName,
        Func<string, bool> predicate)
    {
        var source = candidates.FirstOrDefault(file => predicate(file.FullName));
        if (source is not null)
            CaptureExplicit(captured, outputName, source);
    }

    private static void CaptureExplicit(
        ICollection<(string Name, FileInfo Source, string Text)> captured,
        string outputName,
        FileInfo source)
    {
        if (captured.Any(item => string.Equals(item.Name, outputName, StringComparison.OrdinalIgnoreCase)))
            return;
        try
        {
            captured.Add((outputName, source, ReadTail(source.FullName, MaximumCapturedTextBytes)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ReadTail(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var count = (int)Math.Min(maximumBytes, stream.Length);
        stream.Seek(-count, SeekOrigin.End);
        var buffer = new byte[count];
        _ = stream.Read(buffer, 0, count);
        return Encoding.UTF8.GetString(buffer).TrimStart('\uFFFD');
    }

    private static string BoundText(string text, int maximumBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maximumBytes)
            return text;
        return Encoding.UTF8.GetString(bytes, bytes.Length - maximumBytes, maximumBytes)
            .TrimStart('\uFFFD');
    }

    private static string? FindCrashIdentifier(string text)
    {
        var lunarMatches = LunarCrashIdentifierPattern().Matches(text);
        if (lunarMatches.Count > 0)
            return lunarMatches[^1].Value.ToUpperInvariant();
        var genericMatches = CrashIdentifierPattern().Matches(text);
        return genericMatches.Count > 0
            ? genericMatches[^1].Groups["id"].Value
            : null;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string SanitizePath(string path)
    {
        var result = Path.GetFullPath(path);
        var replacements = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%")
        };
        foreach (var (root, replacement) in replacements
                     .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
                     .OrderByDescending(item => item.Item1.Length))
        {
            if (result.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return replacement + result[root.Length..];
        }
        return result;
    }

    private static void WriteSanitizedJson(string path, object value) =>
        WriteSanitizedText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static void WriteSanitizedText(string path, string text) =>
        File.WriteAllText(path, SanitizeCapturedText(text), new UTF8Encoding(false));

    internal static string SanitizeCapturedText(string text)
    {
        var sanitized = TokenRedactor.Redact(text);
        var replacements = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%")
        };
        foreach (var (root, replacement) in replacements
                     .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
                     .OrderByDescending(item => item.Item1.Length))
        {
            sanitized = sanitized.Replace(root, replacement, StringComparison.OrdinalIgnoreCase);
        }
        sanitized = NamedIdentityPattern().Replace(sanitized, "$1<redacted>");
        sanitized = NamedServerPattern().Replace(sanitized, "$1<redacted>");
        sanitized = NetworkUrlPattern().Replace(sanitized, "<redacted-server>");
        return IpAddressPattern().Replace(sanitized, "<redacted-server>");
    }

    private static void EnforceBundleLimit(string directory, IReadOnlyCollection<string> requiredNames)
    {
        var files = Directory.EnumerateFiles(directory)
            .Select(path => new FileInfo(path))
            .OrderBy(file => requiredNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(file => file.LastWriteTimeUtc)
            .ToList();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= MaximumBundleBytes)
                break;
            if (requiredNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase))
                continue;
            total -= file.Length;
            file.Delete();
        }
    }

    [GeneratedRegex(
        @"(?i)(?:crash(?:\s+|[-_])?(?:id|identifier)|report\s+id)\s*[:=#]?\s*(?<id>[a-z0-9][a-z0-9-]{5,})",
        RegexOptions.CultureInvariant)]
    private static partial Regex CrashIdentifierPattern();

    [GeneratedRegex(@"\bLCLU-[A-Z0-9]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex LunarCrashIdentifierPattern();

    [GeneratedRegex(
        @"(?i)((?:account(?:name)?|user(?:name)?|player(?:name)?)[\w.-]*[\s""]*[:=][\s""]*)([^\s,;""}]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedIdentityPattern();

    [GeneratedRegex(
        @"(?i)((?:server|remote|endpoint|connect(?:ed)?(?:-to)?)[\w.-]*[\s""]*[:=][\s""]*)([^\s,;""}]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedServerPattern();

    [GeneratedRegex(
        @"(?i)\b(?:https?|wss?)://[^\s,;""}]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex NetworkUrlPattern();

    [GeneratedRegex(
        @"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?(?![\w.])",
        RegexOptions.CultureInvariant)]
    private static partial Regex IpAddressPattern();
}
