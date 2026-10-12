# Final Core QA — 2026-10-12

Verdict: **BLOCKED for public release**. The current workspace builds and runs,
but a clean source checkout cannot build the required runtime assets.

## Changes in this pass

- Fixed StorageFormatGuard preventing existing corrupt-index recovery and
  settings fallback. Recognizable unsupported versions still fail closed.
- A null settings document now uses defaults without an automatic migration
  overwrite. Existing corrupt-index preservation and disabled reconstruction
  remain intact.
- Fixed the synthetic partial-account-write test's Windows sharing race using
  bounded atomic replacement; wait for its reader before fixture disposal.
  Production authentication code and account stores were not changed.
- Release safety permits only the three required runtime JAR paths with exact
  SHA-256 pins; arbitrary JARs and modified runtime artifacts remain rejected.
- Repository safety now scans tracked and untracked non-ignored files and
  high-confidence secret signatures without printing their values.
- Replaced personal filesystem paths in existing documentation with
  `%WORKSPACE%` and `%USERPROFILE%`; removed the machine-specific roadmap link.

## Verification

- Locked restore: PASS.
- Release solution build: PASS, zero warnings/errors.
- Full Release tests: **743 passed, zero failed/skipped**. Includes existing
  normal/experimental launch selection, adapters, package/settings persistence,
  owned-process cleanup, repeat launch, official Lunar independence, tray policy,
  launch error diagnostics/redaction and update gates. These are automated
  checks, not a new real Minecraft certification.
- Nine targeted storage/race regressions: PASS on an additional run.
- Self-contained single-file win-x64 publish: PASS.
- Actual release safety block: PASS. A synthetic foreign JAR and wrong-hash
  replacement of the disposable published Cosmetics artifact were rejected;
  original published bytes restored and safety check repeated successfully.
- Production native DLL matched its configured SHA-256.
- Native parser/startup capture/redaction harness: PASS with local MinGW and
  MinHook pinned to `c3fcafdc10146beb5919319d0683e44e3c30d537`.
  MSVC build/reproducibility verification NOT RUN: Visual Studio Build Tools
  unavailable. Production DLL was not replaced.
- Cosmetics rebuilt to its existing production SHA-256; standalone premain,
  transformer, persistence/runtime and service route probes: PASS.
- Published EXE UI startup/render/clean exit: PASS using the existing visual QA
  mode and isolated data root. Screenshot inspected.
- Published EXE second-instance activation: PASS for empty and synthetic
  deeplink arguments using an isolated mutex/pipe receiver; both child
  processes exited with code zero. This verifies IPC forwarding, not a fresh
  tray restore cycle in the normal host.
- Repository secret-signature/private-path scan and diff whitespace check:
  PASS. Signature scanning is not an exhaustive proof of absence of secrets.

Local ignored evidence: `%WORKSPACE%\app\artifacts\final-core-qa` and
`tests/Moonrise.Tests/TestResults/final-core-qa.trx`.

## Remaining release blocker

A copy containing only `git ls-files --cached --others --exclude-standard`
files fails Release build with three **MSB3030** errors:

- `runtime/main-cosmetics-build/moonrise-cosmetics-1.3.2.jar`
- `runtime/mixed-weave-adapter/moonrise-mixed-agent-0.1.0.jar`
- `runtime/mixed-weave-adapter/moonrise-mixed-raven-abi-0.1.0.jar`

These required csproj Content items are ignored and absent from source control.
The current build workflow does not generate them. The mixed adapter build
also requires local pinned loader/Raven inputs, so merely invoking its current
script on a fresh CI runner is insufficient. Public release preparation must
provide a reproducible, reviewed runtime-artifact preparation/distribution path
and verify a clean build without relying on private local package storage.
Omitting these artifacts would break preserved functionality and is not a fix.

## Runtime limits

No new Minecraft launch: the existing official Lunar process group was left
running, with the same seven PIDs before/after. No working installation,
user-owned JAR or account data was modified. Previously completed ordinary,
repeat-launch and mixed integration evidence remains in the existing smoke
documents; Raven+BWH investigation was not repeated.

Fresh normal-host tray/close-during-game cleanup and installed Velopack update/
apply recovery remain unverified in this pass. The existing installed-update
smoke document already records its isolated-environment acceptance steps.
No installer was run, and no commit, push or release publication was made.
