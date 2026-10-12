using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LaunchUiStateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(null)]
    public async Task TerminalStatusResetsOnlyAfterFiveSeconds(int? exitCode)
    {
        var state = new LaunchUiState();
        state.Begin("one");
        if (exitCode.HasValue) state.Exit("one", exitCode);
        state.Complete("one");
        var elapsed = new TaskCompletionSource();
        var reset = state.ResetAfterDelayAsync("one", (duration, token) =>
        {
            Assert.Equal(TimeSpan.FromSeconds(5), duration);
            return elapsed.Task.WaitAsync(token);
        });
        Assert.False(reset.IsCompleted);
        Assert.Equal("complete", state.Phase);
        elapsed.SetResult();
        Assert.True(await reset);
        Assert.Equal("idle", state.Phase);
        Assert.Null(state.ExitCode);
        Assert.False(state.Busy);
    }

    [Fact]
    public async Task CleanupCannotStartResetCountdown()
    {
        var state = new LaunchUiState();
        state.Begin("one");
        state.Exit("one", 0);
        Assert.False(await state.ResetAfterDelayAsync("one", (_, _) => throw new Exception("Delay started during cleanup")));
        Assert.True(state.Busy);
        Assert.Equal("exited", state.Phase);
    }

    [Fact]
    public async Task NewAttemptCancelsResetEvenIfDelayIgnoresCancellation()
    {
        var state = new LaunchUiState();
        state.Begin("old");
        state.Complete("old");
        var elapsed = new TaskCompletionSource();
        CancellationToken cancellation = default;
        var reset = state.ResetAfterDelayAsync("old", (_, token) => { cancellation = token; return elapsed.Task; });
        state.Begin("new");
        Assert.True(cancellation.IsCancellationRequested);
        elapsed.SetResult();
        Assert.False(await reset);
        Assert.False(await state.ResetAfterDelayAsync("old"));
        Assert.True(state.Waiting);
        Assert.Equal("new", state.AttemptId);
    }

    [Fact]
    public async Task ClosingCancelsPendingReset()
    {
        var state = new LaunchUiState();
        state.Begin("one");
        state.Complete("one");
        var reset = state.ResetAfterDelayAsync("one");
        state.CancelReset();
        Assert.False(await reset);
        Assert.Equal("complete", state.Phase);
    }

    [Fact]
    public void WindowStopsWaitingAndExitKeepsGateUntilCleanup()
    {
        var state = new LaunchUiState();
        state.Begin("one");
        Assert.True(state.Waiting);
        Assert.True(state.Running("one"));
        Assert.False(state.Waiting);
        Assert.True(state.Busy);
        Assert.True(state.Exit("one", 0));
        Assert.False(state.Waiting);
        Assert.Equal(0, state.ExitCode);
        Assert.True(state.Busy);
        Assert.False(state.Running("one"));
        Assert.True(state.Complete("one"));
        Assert.False(state.Busy);
    }

    [Fact]
    public void OldWindowExitAndCleanupCannotChangeNewAttempt()
    {
        var state = new LaunchUiState();
        state.Begin("old");
        state.Complete("old");
        state.Begin("new");
        Assert.False(state.Running("old"));
        Assert.False(state.Exit("old", 1));
        Assert.False(state.Complete("old"));
        Assert.True(state.Waiting);
        Assert.Null(state.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExitBeforeWindowClearsWaiting(int code)
    {
        var state = new LaunchUiState();
        state.Begin("one");
        Assert.True(state.Exit("one", code));
        Assert.False(state.Waiting);
        Assert.Equal(code, state.ExitCode);
        Assert.True(state.Complete("one"));
    }
}
