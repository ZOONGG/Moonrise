# Moonrise tray lifecycle — 2026-10-12

## Evidence and scope

- The supplied screenshot shows several Moonrise images; the user reports that only one works and the others disappear later. This is consistent with stale Explorer entries, but the screenshot alone does not identify which previous processes left them.
- One Moonrise process was running before and after a real second launch. The second process exited with code 0; the original PID (22396) remained. The mutex/activation path returns before constructing MainWindow or NotifyIcon.
- Source has one NotifyIcon creation site. Hiding, minimizing and external activation do not recreate it.
- The installed Velopack 1.2.161 API documentation says ApplyUpdatesAndRestart exits immediately and requires cleanup beforehand. Previously tray cleanup existed only in MainWindow_Closed, so update restart bypassed it.
- Normal close also ran settings persistence and other cleanup before deleting the icon. Any exception there could skip deletion. No such exception was reproduced during this task.
- Existing installer/UI probe scripts contain forced Stop-Process calls. Forced termination cannot execute managed cleanup and can leave Explorer images. These scripts were not run or changed here; their involvement in the supplied screenshot is unknown.

## Change

- Initialize the icon once, at the end of successful window construction.
- Delete it first on final window close, with an idempotent Application.OnExit fallback and ProcessExit cleanup for managed immediate exits. No dispatcher wait in ProcessExit.
- Delete before an approved update restart; restore one icon if the updater throws. Blocked restarts leave it untouched.
- Keep existing close/hide BWH policy, minimize, tray Open/Exit and single-instance mutex/pipe behavior.
- No Explorer cleanup, new processes, launch/package/account changes, publishing or Git operations.

## Validation and limits

- Release application and test projects built successfully.
- 25 targeted tests passed: tray disposal with a real Windows NotifyIcon over three cycles, idempotent repeated disposal, update cleanup ordering/failure recovery/blocked restart, existing update tests, and BWH close/hide lifetime policy.
- Real second-launch smoke: 1 process before → secondary exits 0 → same 1 process after.
- git diff --check passed.
- The existing running build was not replaced or stopped. Full visual close/minimize/tray-menu and actual installed-update cycles on the modified application were not run. Explorer entry count was not read directly. Lunar/Minecraft were not launched. No full suite was run.
- Task Manager force-kill, native crash and power loss remain outside managed cleanup guarantees. This patch does not remove already stale Explorer entries.
