using System.Text;
using System.Text.Json;

namespace Moonrise.Services;

public sealed class LaunchSession : IDisposable
{
    private readonly string _tempRoot;
    private bool _disposed;

    internal LaunchSession(string tempRoot, string directory)
    {
        _tempRoot = Path.GetFullPath(tempRoot);
        DirectoryPath = Path.GetFullPath(directory);
    }

    public string DirectoryPath { get; }
    public string BridgeConfigPath => Path.Combine(DirectoryPath, "bridge-config.txt");
    public bool CleanupSucceeded { get; private set; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        EnsureInsideTempRoot(DirectoryPath, _tempRoot);
        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
        CleanupSucceeded = !Directory.Exists(DirectoryPath);
    }

    private static void EnsureInsideTempRoot(string path, string tempRoot)
    {
        var normalizedRoot = tempRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(path).StartsWith("launch-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to clean a path outside the Moonrise launch temp root.");
        }
    }
}

public sealed class LaunchSessionService
{
    public LaunchSession Create(string tempRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);
        var fullRoot = Path.GetFullPath(tempRoot);
        Directory.CreateDirectory(fullRoot);
        var directory = Path.Combine(fullRoot, $"launch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return new LaunchSession(fullRoot, directory);
    }

    public int CleanupStale(string tempRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);
        var fullRoot = Path.GetFullPath(tempRoot);
        if (!Directory.Exists(fullRoot))
            return 0;

        var cleaned = 0;
        foreach (var directory in Directory.EnumerateDirectories(fullRoot, "launch-*", SearchOption.TopDirectoryOnly))
        {
            using var session = new LaunchSession(fullRoot, directory);
            cleaned++;
        }
        return cleaned;
    }
}

public sealed class SanitizedLaunchReport
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly string _path;

    public SanitizedLaunchReport(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        _path = System.IO.Path.Combine(
            logsDirectory,
            $"launch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        Set("moonriseVersion", typeof(SanitizedLaunchReport).Assembly.GetName().Version?.ToString(3) ?? "unknown");
        Set("minecraftVersion", "1.8.9");
        Set("createdUtc", DateTimeOffset.UtcNow.ToString("O"));
        Set("launchStage", "created");
    }

    public string Path => _path;

    public void Set(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _values[name] = value is string text ? TokenRedactor.Redact(text) : value;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
        json = TokenRedactor.Redact(json);
        var temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        TrimLogs(System.IO.Path.GetDirectoryName(_path)!, 50L * 1024 * 1024, _path);
    }

    private static void TrimLogs(string directory, long maximumBytes, string currentReport)
    {
        var files = Directory.EnumerateFiles(directory)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        var retainedBytes = 0L;
        foreach (var file in files)
        {
            retainedBytes += file.Length;
            if (retainedBytes <= maximumBytes ||
                string.Equals(file.FullName, currentReport, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            file.Delete();
        }
    }
}
