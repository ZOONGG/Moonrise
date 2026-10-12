# OfficialLunar repeat launch after WM_CLOSE

Build: `%WORKSPACE%\app\artifacts\repeat-launch-framework\Moonrise.exe`.
Release, win-x64, framework-dependent (.NET 8 Windows Desktop required). Keep the entire output folder.

## Evidence and implementation

Inspected installed Lunar Client 3.7.23-ow (ProductVersion 3.7.23.0), read-only `resources/app.asar`.
ASAR SHA-256: `ade7fcd6d2def6a8688e21d5cad04a74a5db858fb281c7accf7b2249ef3227fb`.
Source location: `dist-electron/electron/main.js` inside the archive.

The main window close handler prevents default, hides the window and only quits when exit-to-dock is disabled and no other browser remains. Exit-to-dock defaults to true. WM_CLOSE therefore is not an application shutdown contract; one disappearing PID does not prove the singleton exited.

The production-enabled `close-launcher` deeplink route sets the closing flag and calls Electron app.quit(), as does Quit Launcher in the tray menu. The before-quit handler calls tray cleanup/destroy and application module cleanup. Second-instance dispatch handles lunarclient:// arguments; the sender fails the singleton lock and exits before tray initialization. No registry protocol invocation or Lunar file/settings mutation is needed.

Moonrise records the PID, exact start time and executable path of the root it starts, independently of the window watcher's attempt-scoped ownership. At the next launch it verifies that root and all matching Electron processes belong to its tree, rejects other Lunar installations/roots and any remaining child JVM (including one without a window), then revalidates immediately before dispatching `lunarclient://close-launcher` through the existing singleton sender mechanism. It waits for sender exit and for all Lunar processes to disappear before preparing another launch. There is no force fallback on this path. An unowned, ambiguous or unresponsive launcher blocks the next launch.

Ownership is intentionally in-memory. A Lunar left by an older Moonrise build or a previous Moonrise process is unowned and must be closed normally once before this smoke. A changed Lunar binary may stop accepting the command; that remains fail-closed.

Reuse is not implemented: the native bridge obtains MOONRISE_BRIDGE_CONFIG from the launcher environment and uses the launch-session configuration path. Completed sessions are cleaned; there is no supported bridge reconfiguration handshake to prove reuse across sessions with different packages/configuration.

## Manual smoke (do not start two Moonrise copies)

1. Close the old Moonrise normally. For the initial baseline only, close any already running Lunar through its tray Quit Launcher command. Record existing icons, including ghosts, without restarting Explorer or clearing the tray. Open the new build above.
2. Launch the usual Minecraft 1.8.9 profile with unchanged Veyra + BWH. Confirm mods, official account and game work. Record Lunar root PID and tray icon count. Close Minecraft normally; wait for Moonrise to report normal exit/JVM exit 0.
3. Press Launch again without closing Lunar. Expect `Lunar shutdown mechanism: close-launcher deeplink`, successful graceful close, `forced=[]`, `remaining=[]`, then a new controlled root and successful game launch. Record roots and icons before/after; close Minecraft normally and wait for exit 0.
4. Repeat a third time, again without manually closing Lunar. Expect the same success and at most one active root. Record root PIDs, icons and normal JVM exit.
5. Open the controlled Lunar normally and verify account selection/switching through official Lunar remains functional. Include a selected-account change before a subsequent launch if multiple accounts are available. Do not edit account files.
6. Check for new ghost icons only after recording counts: an extra icon disappearing on hover is a ghost. Stop and record a failed smoke if any launch blocks, roots accumulate, a new ghost appears, or authorization breaks. Do not kill processes to continue.

Negative checks: while a game is active, another launch must block. After restarting Moonrise with Lunar left running, that unowned launcher must remain untouched and block dispatch. No mass Electron termination is allowed.

Automated verification: targeted lifecycle/termination/ownership/environment/JVM tests, including three synthetic cycles, foreign roots/installations, reused PID, hidden Java, missing tree evidence and quit timeout. These do not prove actual Electron tray behavior or Minecraft integration. Codex did not launch Lunar or Minecraft for this task.
