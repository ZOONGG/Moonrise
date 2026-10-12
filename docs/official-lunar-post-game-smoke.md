# OfficialLunar post-game shutdown

Build: `%WORKSPACE%\app\artifacts\official-lunar-shutdown\Moonrise.exe`.
Keep the complete build folder. Close the previous Moonrise normally before starting this build.

## Read-only evidence

Installed Lunar 3.7.23-ow, `resources/app.asar`, SHA-256
`ade7fcd6d2def6a8688e21d5cad04a74a5db858fb281c7accf7b2249ef3227fb`.
In `dist-electron/electron/main.js`, `qWe` (JVM exit) calls `callbacks.reset()`.
`iGe` (reset) calls `K9.focusMain()` unless the initiator is Mission Control.
This happens before the normal exit/crash branch. Moonrise does not reveal Lunar
on normal exit or JVM crash. Its previous watcher released window ownership at
Minecraft detection, and controlled quit previously ran only at the next launch.

The production `close-launcher` deeplink sets the closing flag and calls
Electron `app.quit()`. Before-quit cleans the tray and application modules.
WM_CLOSE can instead hide to the dock. Multiple Electron child processes do not
prove multiple launchers; the previous smoke did not establish whether extra
icons were ghosts. No installed Lunar files or settings were changed.

No verified launch-deeplink option to disable reset/focus was found. Moonrise
keeps its existing Windows hide hook for verified owned windows through the game
lifetime and graceful quit. The hook reacts to show events; it cannot guarantee
that no frame ever flashes. That requires the real smoke below. The Mission
Control internal initiator exception is not treated as a supported external API.

## Manual smoke

1. Record Lunar root PIDs and tray icon count. Close any pre-existing Lunar with
   its normal Quit Launcher action; do not kill it, restart Explorer or clear icons.
2. Start the new Moonrise and launch the usual Minecraft 1.8.9 configuration.
3. Close Minecraft normally. Observe whether Lunar appears, including a brief
   flash. Wait up to 25 seconds for cleanup and record Lunar PIDs and icon count.
4. Check the sanitized report: `javaExitCode=0`, `launchStage=normal-exit`,
   `postGameOwnedRootPid`, `postGameGracefulShutdownSuccess=true`,
   `postGameQuitCommandCompleted=true`, `postGameRemainingProcessIds=[]`, and
   `postGameVisibleLauncherWindows=0`. The window classification log records
   owned show events and whether hiding succeeded.
5. Launch Minecraft again, then close it normally. Confirm the same cleanup and
   no growth of root instances or icons. If cleanup fails, retain the report;
   do not force-kill processes to make the second launch pass.

When CloseAfterLaunch is enabled, Moonrise remains hidden in its tray so it can
perform this cleanup. Explicitly quitting Moonrise during the game stops its
in-process lifetime monitoring; this is not a background daemon.

Automated validation: 91 targeted tests passed; Release build passed with zero
warnings/errors; `git diff --check` passed. Minecraft and real Electron shutdown
were not run by Codex. Real no-flash/tray/resource behavior remains unverified.
