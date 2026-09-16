namespace Moonrise.Models;

public sealed record ProcessRecord(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    long MainWindowHandle,
    string MainWindowTitle,
    bool IsMainWindowVisible,
    bool IsResponding);
