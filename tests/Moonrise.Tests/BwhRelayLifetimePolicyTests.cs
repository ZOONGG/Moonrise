using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class BwhRelayLifetimePolicyTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StandaloneLaunchOrCleanupKeepsHostUntilExplicitExit(bool launchBusy, bool cleanupPending)
    {
        Assert.True(BwhRelayLifetimePolicy.ShouldHideInsteadOfClose(false, false, launchBusy, cleanupPending));
        Assert.False(BwhRelayLifetimePolicy.ShouldHideInsteadOfClose(false, true, launchBusy, cleanupPending));
        Assert.False(BwhRelayLifetimePolicy.ShouldHideInsteadOfClose(false, false, false, false));
    }

    [Fact]
    public void NormalWindowClose_HidesWhileRelayIsRunning()
    {
        Assert.True(BwhRelayLifetimePolicy.ShouldHideInsteadOfClose(
            relayRunning: true,
            explicitExitRequested: false));
        Assert.False(BwhRelayLifetimePolicy.ShouldHideInsteadOfClose(
            relayRunning: true,
            explicitExitRequested: true));
        Assert.False(BwhRelayLifetimePolicy.ShouldHideInsteadOfClose(
            relayRunning: false,
            explicitExitRequested: false));
    }

    [Fact]
    public void RelayStopsOnlyAfterLastGameAndOutsideAnotherLaunch()
    {
        Assert.True(BwhRelayLifetimePolicy.ShouldStopAfterGameExit(
            activeGameCount: 0,
            launchInProgress: false));
        Assert.False(BwhRelayLifetimePolicy.ShouldStopAfterGameExit(
            activeGameCount: 1,
            launchInProgress: false));
        Assert.False(BwhRelayLifetimePolicy.ShouldStopAfterGameExit(
            activeGameCount: 0,
            launchInProgress: true));
    }

    [Fact]
    public void HiddenCloseAfterLaunchHost_ExitsWhenRelayStops()
    {
        Assert.True(BwhRelayLifetimePolicy.ShouldExitHiddenHost(
            windowVisible: false,
            closeAfterLaunch: true));
        Assert.False(BwhRelayLifetimePolicy.ShouldExitHiddenHost(
            windowVisible: true,
            closeAfterLaunch: true));
    }
}
