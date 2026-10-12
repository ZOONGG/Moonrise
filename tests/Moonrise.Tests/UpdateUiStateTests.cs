using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class UpdateUiStateTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DisplayDoesNotInspectProcessesWithoutAnEnabledRestart(bool enabled, bool pending)
    {
        var state = UpdateUiState.ForDisplay(enabled, false, true, pending,
            () => throw new InvalidOperationException("Process inspection must not run."));
        Assert.False(state.GameActive);
        Assert.False(state.CanRestart);
    }

    [Fact]
    public void EnabledPendingRestartInspectsGameOnceAndRemainsBlocked()
    {
        var calls = 0;
        var state = UpdateUiState.ForDisplay(true, false, false, true, () => { calls++; return true; });
        Assert.Equal(1, calls);
        Assert.True(state.GameActive);
        Assert.False(state.CanRestart);
    }

    [Theory]
    [InlineData(false, false, false, false, false, false, false, false)]
    [InlineData(false, false, true, true, false, false, false, false)]
    [InlineData(true, false, false, false, false, true, false, false)]
    [InlineData(true, false, true, false, false, true, true, false)]
    [InlineData(true, false, true, false, true, true, true, false)]
    [InlineData(true, true, true, false, false, false, false, false)]
    [InlineData(true, false, true, true, false, true, false, true)]
    [InlineData(true, false, true, true, true, true, false, false)]
    [InlineData(true, true, true, true, false, false, false, false)]
    [InlineData(true, false, false, true, false, true, false, true)]
    public void ActionsFollowInstalledBusyDownloadAndGameState(bool enabled, bool busy,
        bool available, bool pending, bool gameActive, bool check, bool download, bool restart)
    {
        var state = new UpdateUiState(enabled, busy, available, pending, gameActive);
        Assert.Equal(check, state.CanCheck);
        Assert.Equal(download, state.CanDownload);
        Assert.Equal(restart, state.CanRestart);
    }

    [Fact]
    public void UnrelatedAndRecoveredJavaProcessesDoNotBlockUpdate()
    {
        Assert.False(UpdateUiState.IsGameActive(false, new HashSet<int> { 100 }, new[] { 200, 300 }));
        Assert.True(UpdateUiState.IsGameActive(false, new HashSet<int> { 100 }, new[] { 100, 200 }));
        Assert.False(UpdateUiState.IsGameActive(false, new HashSet<int> { 100 }, Array.Empty<int>()));
        Assert.True(UpdateUiState.IsGameActive(true, new HashSet<int>(), Array.Empty<int>()));
    }

    [Fact]
    public void DownloadFailureAllowsRetryAndNeverOffersRestart()
    {
        var downloading = new UpdateUiState(true, true, true, false, false);
        Assert.False(downloading.CanDownload);
        var failed = downloading with { Busy = false };
        Assert.True(failed.CanDownload);
        Assert.False(failed.ShowRestart);
    }

    [Fact]
    public void GameExitReenablesRestartForAlreadyStagedUpdate()
    {
        var playing = new UpdateUiState(true, false, false, true, true);
        Assert.True(playing.ShowRestart);
        Assert.False(playing.CanRestart);
        Assert.True((playing with { GameActive = false }).CanRestart);
    }
}
