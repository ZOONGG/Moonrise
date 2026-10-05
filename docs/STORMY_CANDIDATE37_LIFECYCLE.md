# Candidate 37 launch/window evidence

The 20261005-123710 bundle contains two distinct outcomes. Source baseline is
6c32dd7 (MNR4 Candidate 37/38), not the divergent local checkout 9b8427c.

## Current timeout (12:33:40–12:37:10, UTC+06)

- Moonrise launcher root PID 19420; observed launcher tree:
  5756, 8076, 9788, 10016, 17464, 19420, 19496.
- The second-instance deeplink arrived at 12:33:54.772. At 12:33:54.774,
  Lunar main.log reports `Attempted to launch without metadata, skipping...`.
- Lunar versions metadata arrived at 12:34:00.845, virtual profiles at
  12:34:00.847, and selected 1.8.9 profile at 12:34:00.861.
- Moonrise observed no launch Java PID or Minecraft HWND. Lunar contains no
  JVM spawn for this attempt. This timeout is a readiness race, not a replaced
  JVM or usable-window classifier failure.
- Candidate 37 accepted the early structured startup profile or renderer-ready
  fallback, before launch metadata existed. Require metadata plus virtual-profile
  setup before accepting profile evidence. Preserve rotation/checkpoint handling.

## Crash LCLU-CBWNBIHNVYQX belongs to the preceding attempt

- Lunar main.log started JVM PID 13248 at 12:29:28.372; Moonrise observed its
  parent PID 20088 and later exit code 1. Lunar recorded exit at 12:30:04.182.
- ichor-boot.log reached Genesis game bootstrap, Minecraft 1.8.9, and Ichor Mixin
  initialization. latest.log at 12:30:03 shows Weave initialization followed by
  `IllegalStateException: Active mixin service is NOT WeaveMixinService`, in
  `Genesis//net.weavemc.loader.WeaveLoader.init(WeaveLoader.kt:43)`.
- This is a game bootstrap JVM, failing before a usable game window was observed.
  No evidence proves that a transient Minecraft HWND was ever created.
- Lunar uploaded this crash at 12:32:37.940, before the current attempt started.
  Previously, an appended shared log could attribute that stale ID to a later
  timeout. Ignore timestamped crash entries outside the attempt interval.
- The Weave/Ichor Mixin-service conflict remains unresolved. Neither Stormy
  injection success nor runtime compatibility is claimed. No JAR was modified,
  no Weave generation changed, and the mixed-generation guard remains intact.

## Window evidence and correction

The classification trace identifies hidden Chrome_WidgetWin_0 (PID 9788),
Electron_NotifyIconHostWindow (PID 19420), and owned IME/TSF windows alongside the
actual Chrome_WidgetWin_1 launcher (PID 19420, title Lunar Client). Previously
every owned-tree HWND qualified for Restore; this explains revealing technical
windows. The bundle does not identify which HWND initially produced a black
flash, so there is no evidence for changing Process.Start console flags.

Reveal now restores and focuses exactly one selected launcher HWND. It requires
verified attempt ownership, matching launcher executable, no Chromium process
type, Chrome_WidgetWin_1, no owner/tool/child semantics, and dimensions at least
400 by 250. A hidden structurally valid launcher remains eligible for background
launch. Title is not a selector. Helpers never qualify through ancestry alone.
Selection decisions log HWND, PID/process, sanitized title/class, visibility,
owner HWND/PID, size, styles and acceptance/rejection reason during the watcher
and on Show Lunar. Existing hide/watcher ownership protections remain unchanged.

## Validation boundaries

Targeted readiness/rotation, single-window control, Java lifecycle and Stage 2/3
recovery tests and a Release build are required before the candidate push.
The next real smoke must verify main-window dimensions/semantics, absence of
technical-window flashes and the remaining Weave/Ichor failure. Unit tests and
builds do not establish successful Stormy runtime compatibility.
