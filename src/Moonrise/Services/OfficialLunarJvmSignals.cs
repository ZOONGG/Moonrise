using System.Text;
using System.Text.RegularExpressions;

namespace Moonrise.Services;

// Reads only lifecycle fields after this launch's dispatch checkpoint. Never exports raw log lines.
internal sealed class OfficialLunarJvmSignals : IDisposable
{
    private readonly string _path;
    private FileStream? _stream;
    private DateTime _creation;
    private long _offset;
    public OfficialLunarJvmSignals(string path, long checkpoint)
    {
        _path = path;
        _offset = checkpoint;
        TryOpen();
    }
    private void TryOpen()
    {
        try
        {
            _creation = File.GetCreationTimeUtc(_path);
            _stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose() => _stream?.Dispose();
    private string _pending = "";
    private bool _starting, _exiting;
    private string? _session;
    public int? ProcessId { get; private set; }
    public int? ExitCode { get; private set; }
    public void Read()
    {
        try
        {
            if (_stream is null) TryOpen();
            Drain();
            if (_stream is not null && File.Exists(_path) && File.GetCreationTimeUtc(_path) != _creation)
            {
                // Retain the original file handle so a short JVM exit just before log rotation is not lost.
                _stream.Dispose();
                _stream = null;
                _offset = 0;
                _pending = "";
                TryOpen();
                Drain();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void Drain()
    {
        if (_stream is null) return;
        if (_stream.Length < _offset) { _offset = 0; _pending = ""; }
        _stream.Position = _offset;
        using var reader = new StreamReader(_stream, Encoding.UTF8, true, leaveOpen: true);
        Feed(reader.ReadToEnd());
        _offset = _stream.Position;
    }
    internal void Feed(string text)
    {
        _pending += text;
        var end = _pending.LastIndexOf('\n');
        if (end < 0) return;
        var complete = _pending[..(end + 1)];
        _pending = _pending[(end + 1)..];
        foreach (var line in complete.Split('\n'))
        {
            var session = Regex.Match(line, @"\[Launch\] Starting new session \(ID: ([0-9a-f-]{36})\)");
            if (session.Success && _session is null) _session = session.Groups[1].Value;

            if (ProcessId is null && line.Contains("[Launch] JVM process started with following outputs...", StringComparison.Ordinal))
            { _starting = true; _exiting = false; }
            var pid = Regex.Match(line, @"\* PID: (\d+)\s*$");
            if (_starting && pid.Success && int.TryParse(pid.Groups[1].Value, out var id))
            { ProcessId = id; ExitCode = null; _starting = false; }
            if (ProcessId is not null && line.Contains("[Launch] JVM for launch session ", StringComparison.Ordinal) &&
                line.Contains(" has exited...", StringComparison.Ordinal) &&
                _session is not null && line.Contains($"session {_session} has exited", StringComparison.Ordinal)) _exiting = true;
            var code = Regex.Match(line, @"\* Code: (-?\d+)\s*$");
            if (_exiting && code.Success && int.TryParse(code.Groups[1].Value, out var value))
            { ExitCode = value; _exiting = false; }
        }
    }
}

internal sealed class MinecraftLaunchOutcome
{
    public string? State { get; private set; }
    public int? ExitCode { get; private set; }
    public string? Decide(int? exitCode, bool usableWindow, bool deadline, bool jvmEvidence)
    {
        if (State is not null) return State;
        ExitCode = exitCode;
        State = exitCode is not null ? exitCode == 0 ? "normal-exit" : "java-exited-nonzero"
            : usableWindow ? "launched"
            : deadline ? jvmEvidence ? "java-startup-unconfirmed" : "minecraft-timeout" : null;
        return State;
    }
    internal static bool IsCrash(int? code) => code is not null and not 0;
}
