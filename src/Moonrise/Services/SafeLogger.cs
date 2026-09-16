namespace Moonrise.Services;

public sealed class SafeLogger
{
    private readonly string _path;
    private readonly object _gate = new();

    public SafeLogger(string directory, string filePrefix = "moonrise")
    {
        Directory.CreateDirectory(directory);
        var safePrefix = string.Concat(filePrefix.Where(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safePrefix))
            safePrefix = "moonrise";
        _path = System.IO.Path.Combine(
            directory,
            $"{safePrefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.log");
    }

    public string Path => _path;
    public void Write(string message)
    {
        lock (_gate)
            File.AppendAllText(_path, $"[{DateTimeOffset.Now:O}] {TokenRedactor.Redact(message)}{Environment.NewLine}");
    }
}
