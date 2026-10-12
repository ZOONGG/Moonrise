# Stage 1 Core validation — 2026-10-12

Stage 1 remains open. This checklist tracks the remaining Core acceptance work;
it is not a replacement roadmap or a claim of universal package compatibility.

## Current verification

- Release application/test build: PASS, zero warnings/errors.
- 99 targeted tests: PASS, zero skipped. Scope: tray disposal, launch UI,
  Lunar lifecycle/termination, Minecraft process tracking, official JVM lifecycle,
  session cleanup ownership, experimental adapter selection, launch plans and
  Stage 1 recovery.
- All 15 original library JAR SHA-256 values match the metadata index.
- A further 74 targeted tests passed: maintained-build resolution, standalone
  legacy hooks selection, bridge deployment, local library, synthetic adapter,
  delayed Minecraft detection and linkage diagnostics. Total: 173 targeted tests.
- Library selection observed unchanged: BWH, unrestricted, Veyra and Cosmetics
  enabled; Raven and the remaining packages disabled. Actual launch selection
  also depends on the existing settings exclusions.
- New local framework-dependent validation build prepared at
  `%WORKSPACE%\builds\Moonrise-core-validation-20261012`.
  It includes the existing NotifyIcon lifecycle fix and existing runtime assets.
  No installer, release or deployment was made.

## Existing bounded runtime evidence

The existing mixed investigation and main-integration logs contain prior real
Lunar startup evidence. Prior user feedback confirms basic Raven/BWH coexistence,
the full Raven+BWH+unrestricted+Veyra+Cosmetics set after the Cosmetics 1.3.1 fix,
and ordinary-mode operation. These results are retained; they do not establish
standalone behavior or certify every function of each package.

Normal mixed-generation rejection and the experimental switch remain in place.
The experimental legacy ABI allowlist is bounded to the exact original Raven
hash. BWH/unrestricted/Cosmetics resolution uses explicitly identified maintained
runtime builds; those results must not be described as direct original execution.

## Remaining acceptance checks

Standalone Veyra: PASS. Actual original hash `779760392bca1f269d0eb325e78950249d355e94a09c90988108c3ccd94d95bd`,
one mod, zero package agents, Weave 1.3.4, experimental mode disabled. User
confirmed menu/function operation. Session
`launch-92531b8743a946df976e938ca176dee3` completed with `normal-exit`, JVM exit 0,
cleanup success, graceful Lunar shutdown, remaining `[]`, zero visible launcher
windows; session removed and Moonrise retained. Evidence:
`%LOCALAPPDATA%\Moonrise\dev\experiments\bwh-baseline\logs\core-veyra-only-20261012`.

Cosmetics standalone: user-confirmed functional PASS after the 1.3.2 packaging
fix (see `cosmetics-standalone-core-fix.md`). Closing Moonrise before its game
monitor completed reproduced an interrupted report/session and a hidden Lunar
launcher. The next ordinary launch correctly stopped at already-running Lunar.
That experiment-owned launcher was shut down with the supported close-launcher
deeplink, without force termination. The existing hide/close policy now keeps
the tray host during any busy launch or pending cleanup, with explicit Exit
preserved. Release build and 29 targeted lifecycle tests passed; a real corrected
standalone close cycle is running. Ordinary 1.3.2 regression remains pending.

Update: ordinary 1.3.2 regression is PASS. User confirmed mod/cosmetics operation;
session `launch-8b477ff5172e49f6ad973d3fb46de6ce` finished with JVM exit 0,
`normal-exit`, successful cleanup, graceful Lunar shutdown, remaining processes
`[]` and zero visible launcher windows. The session directory was removed and
Moonrise remained running. A repeat launch from that same instance is requested.
The standalone close-cycle user confirmed hiding, then explicitly used tray
Exit during cleanup; completed standalone cleanup is still unverified.

Repeat ordinary-mode cycle: PASS. From the same Moonrise PID 23988, report
`launch-20261012-134121-14d789444ff04b869febe50e8bc296d6.json` and session
`launch-9521abb3116e46eba5016342b1d4362c` completed with JVM exit 0,
`normal-exit`, cleanup success, graceful Lunar shutdown, remaining `[]` and zero
visible launcher windows. The session directory is absent; only Moonrise
remains. The user also confirmed the second launch worked normally.

- Real standalone launches and additional selected combinations, with explicit
  original/runtime identities and agent readiness evidence.
- Repeated Launch while preparing/running, already-running unowned Lunar,
  game close/relaunch, Moonrise close during a game, failed startup/recovery.
- Real graceful Lunar shutdown, no accumulating roots, session cleanup and
  absence of unexpected window conflicts.
- Visual tray hide/open/close on the new validation binary.
- Roadmap: PASS. The user identified the existing authoritative roadmap at
  `%USERPROFILE%\Desktop\ㅤ\Moonrise-Roadmap.md`. It retains all five stages,
  Stage 1 in progress and voluntary Cosmetics Network in Stage 3. Clarified the
  own-service/Cosmetics-agent wording there; README now links to that existing
  file instead of duplicating its stage descriptions.

## New real ordinary-mode smoke

Launch report `launch-20261012-130811-8db58108ac014e92936742712645cf64.json`
records three mods and one package agent, experimental mode disabled. Cosmetics
1.3.1 emitted `cosmetics-agent.ready`. Actual Lunar 1.8.9 window responded.
After normal game closure: JVM exit 0, `normal-exit`, cleanup success, graceful
Lunar shutdown success, no remaining owned processes and zero visible launcher
windows. Temporary session `launch-ab14d39fb02048d0809f7b74b0c051ac` was removed.
Existing auth-service connectivity failures remain in sanitized JVM logs.

The user answered “все гуд” to the requested repeat-launch/tray/manual cycle.
This is user-reported visual evidence; only one new completed launch report was
observed at that point, so two logged cycles are not asserted. On resumption both
Moonrise and Minecraft were closed. No forced termination was used.

## Current external limitations

Computer-use capture of the existing Moonrise window failed with
`FrameArrived timed out`; fresh window discovery/activation and capture retry
failed with `window capture timed out`. No automated screenshot verification
exists. Manual verification was reported successful by the user. The subsequent
computer-use request was stopped with physical Escape; work resumed after the
user explicitly requested continuation.

The existing Desktop roadmap remains authoritative. No duplicate roadmap or
mod profiles were created. Cosmetics Network remains future Stage 3 scope, and
visibility to unmodified Lunar remains unconfirmed.
