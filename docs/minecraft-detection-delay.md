# Minecraft running-state detection delay

Local measurements, 2026-10-09, without launching Minecraft:

- Original full process snapshot: 2399–2545 ms across 330 processes.
- Checking `Process.Responding` separately: 1 ms total across 11 windows;
  no slow response probes in this observation. A hung window remains a possible
  additional delay, but was not the measured cause here.
- Skipping window checks and executable paths alone was insufficient:
  snapshots still took 1659–1767 ms across 341 processes.
- Avoiding repeated `Process.GetProcessById` for unrelated processes and reading
  creation times directly with `OpenProcess`/`GetProcessTimes`: 6–7 ms across
  334 processes. Creation-time identity and ancestry are retained.

The monitor requires six consecutive visible, titled, responsive game-window
samples. The 100 ms polling interval previously also included the full snapshot
cost each time. There is no explicit 30-second success delay. These measurements
demonstrate a snapshot bottleneck, but do not reproduce the exact reported
30-second delay: there were no Java windows in the measured snapshots.

Only the game monitor opts into the lightweight snapshot. Other snapshot callers
and Lunar window suppression retain their existing behavior. Game-window response
checks use a bounded 100 ms WM_NULL probe. A runtime Java PID alone cannot confirm
success: the usable window must also have the LWJGL/GLFW30 class. Six consecutive
samples and exit-before-success priority remain unchanged.

The running state and status are updated before window-monitor ownership transfer
and report saving. WPF is allowed to render before those operations.

Diagnostics emit one record per stage, with attempt ID, UTC timestamp, and
milliseconds since monitoring began:

1. `java-process-observed`
2. `minecraft-window-detected`
3. `usable-window-confirmed`
4. `launch-state-updated`
5. `ui-notification-dispatched`

One initial `snapshotMs` measurement is also logged. No titles, paths, log contents,
or command-line arguments are added to these diagnostics.

Validation: 24 targeted tests pass for window detection, bounded response probes,
creation-time identity, PID reuse/ancestry, interrupted confirmation, immediate
running-state transition, stale attempts, early JVM crash, and normal exit.
Release build passes; `git diff --check` passes. Real Lunar/Minecraft smoke test
has not been performed.

## Manual check

Open the local build in `builds/minecraft-detection-delay/Moonrise.exe` with the
usual Moonrise configuration and initiate a normal launch yourself. Observe the
time from the first usable game window to “Игра запущена”. The unchanged six-sample
confirmation should ordinarily take about 0.6 seconds plus game-window snapshot
and UI scheduling time; this is an expectation, not a real-game measurement.

Compare the five `Launch detection` records in Diagnostics. The interval from
`usable-window-confirmed` to `ui-notification-dispatched` should be minimal.
Close Minecraft normally and confirm the normal closed status. Do not infer
package functionality from the running status.
