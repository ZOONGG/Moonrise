namespace Moonrise.Services;

// Presentation policy only; UpdateService rechecks the activity gate before handing off.
internal readonly record struct UpdateUiState(bool Enabled, bool Busy, bool Available, bool Pending, bool GameActive)
{
    // Process inspection can wait on other applications. Do not run it for a
    // disabled updater or when there is no restart action to display.
    public static UpdateUiState ForDisplay(bool enabled, bool busy, bool available,
        bool pending, Func<bool> gameActive) =>
        new(enabled, busy, available, pending, enabled && pending && gameActive());

    public static bool IsGameActive(bool launching, IReadOnlySet<int> managed, IEnumerable<int> active)
        => launching || active.Any(managed.Contains);

    public bool CanCheck => Enabled && !Busy;
    public bool ShowDownload => Enabled && Available && !Pending;
    public bool CanDownload => ShowDownload && !Busy;
    public bool ShowRestart => Enabled && Pending;
    public bool CanRestart => ShowRestart && !Busy && !GameActive;
}
