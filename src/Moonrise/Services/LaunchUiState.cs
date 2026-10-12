namespace Moonrise.Services;

internal sealed class LaunchUiState
{
    internal static readonly TimeSpan TerminalDisplayDuration = TimeSpan.FromSeconds(5);
    private CancellationTokenSource? _resetCancellation;
    public string? AttemptId { get; private set; }
    public string Phase { get; private set; } = "idle";
    public int? ExitCode { get; private set; }
    public bool Waiting => Phase == "starting";
    public bool Busy => Phase is "starting" or "running" or "exited";
    public void Begin(string id) { CancelReset(); AttemptId = id; Phase = "starting"; ExitCode = null; }
    public void CancelReset() => _resetCancellation?.Cancel();

    public async Task<bool> ResetAfterDelayAsync(string id,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (!IsCurrent(id) || Phase != "complete") return false;
        CancelReset();
        using var cancellation = new CancellationTokenSource();
        _resetCancellation = cancellation;
        try
        {
            await (delay ?? Task.Delay)(TerminalDisplayDuration, cancellation.Token);
            if (cancellation.IsCancellationRequested || !IsCurrent(id) || Phase != "complete") return false;
            Phase = "idle";
            ExitCode = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
        finally
        {
            if (ReferenceEquals(_resetCancellation, cancellation)) _resetCancellation = null;
        }
    }
    public bool IsCurrent(string id) => AttemptId == id;
    public bool Running(string id)
    {
        if (!IsCurrent(id) || Phase != "starting") return false;
        Phase = "running";
        return true;
    }
    public bool Exit(string id, int? code)
    {
        if (!IsCurrent(id) || !Busy || Phase == "exited") return false;
        Phase = "exited"; ExitCode = code;
        return true;
    }
    public bool Complete(string id)
    {
        if (!IsCurrent(id) || !Busy) return false;
        Phase = "complete";
        return true;
    }
}
