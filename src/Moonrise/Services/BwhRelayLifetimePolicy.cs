namespace Moonrise.Services;

internal static class BwhRelayLifetimePolicy
{
    internal static bool ShouldHideInsteadOfClose(bool relayRunning, bool explicitExitRequested) =>
        relayRunning && !explicitExitRequested;

    internal static bool ShouldStopAfterGameExit(int activeGameCount, bool launchInProgress) =>
        activeGameCount == 0 && !launchInProgress;

    internal static bool ShouldExitHiddenHost(bool windowVisible, bool closeAfterLaunch) =>
        !windowVisible && closeAfterLaunch;
}
