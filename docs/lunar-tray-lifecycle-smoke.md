# OfficialLunar tray lifecycle smoke

Build under test: `artifacts/tray-framework/Moonrise.exe` (Release, framework-dependent).
Close the currently running Moonrise normally before opening this build. Do not run two Moonrise copies.
Do not clear the tray or restart Explorer. Leave any existing ghost icons in place and record the baseline.

1. Record the initial Lunar tray icon count and Lunar root PIDs. An Electron root and its children are one launcher, not separate instances. Start Minecraft through Moonrise with the usual profile/account and hidden Lunar launch. Check the account and Minecraft work, then close Minecraft normally through its menu. Wait 10 seconds and record icon count and root PIDs again.
2. Repeat the same launch and normal game close. Check that preflight either closes the previous hidden launcher gracefully or reports that it remains running. If blocked, record this as a failed smoke test; do not force-kill Lunar to continue.
3. Repeat once more. Compare all three cycles: no growing root-instance count, no growing icon count, no new ghost icons after normal launcher exit. After recording the count, hovering an extra icon that disappears identifies a ghost; a remaining icon alone does not prove another active instance.

Expected diagnostics: `lunarForceTerminatedProcessIds` is empty for preflight; any remaining Lunar blocks creation of a new launcher. The normal IPC sender exits without fallback. If fallback occurs, `Lunar IPC sender close` records `forced=True` for the sender owned by that operation only.

If Lunar treats WM_CLOSE as minimize-to-tray rather than quit, report the blocked second launch and the sanitized preflight/close PID diagnostics. This behavior requires a supported Lunar quit mechanism; it must not be worked around with mass termination.

Automated checks do not establish real Electron tray behavior. Minecraft was not started by Codex.
