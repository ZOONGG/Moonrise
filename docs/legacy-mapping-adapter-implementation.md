# Integration fix verified offline — 2026-10-08

Supersedes the pending runtime statements below. Manual Minecraft smoke is still pending.

- Reproduced on the pinned, actual Genesis class: `addURL` registers both URLClassLoader
  and Ichor resource routes; `getResources` returns the identical JAR-entry URL twice.
  The old count check rejected one real resource. The adapter now requires exactly one
  distinct URL, equal to the generated session JAR entry, verifies all enumerated bytes,
  the selected URL, and the actual defining loader's `getResourceAsStream` bytes.
  Zero resources, wrong bytes and two different origins are rejected.
- Continued through real Mixin 0.8.7 startup. Legacy Weave's transformer supplies
  `getDefaultEnvironment`, which Mixin 0.8.7 rejects if it differs from the current
  environment. The separately loaded adapter changes that single invocation to
  `getCurrentEnvironment` in memory. No on-disk Weave/Genesis/Lunar JAR is changed.
- Adapter 1.1.0 records service constructor completion, successful service check,
  init completion and the original init exception (rethrown unchanged).
- Offline JVM PID 19864, Lunar Java 17.0.18: actual isolated MappingAgent, actual
  Genesis defining loader and native Ichor URL registration, untouched Weave 0.2.6
  premain/accessor transformation, Mixin 0.8.7 selection, actual XSrgMapper parse
  and representative mapping, actual service bytecode-provider remapping of the
  saved Minecraft fixture and a post-init transformer call, zero mods, `WeaveLoader.init()` returned, JVM exit 0.
  The harness uses an empty Ichor stage/config and saved bytecode fixtures; it does
  not invoke a Minecraft entrypoint or establish live OfficialLunar success.
  Mixin reports late subsystem initialization in this harness; no mod mixins are tested.
  Forge's existing host library supplies commons-lang3 in the offline classpath.
- Release build: 0 warnings/errors. 24 focused .NET tests pass; actual Genesis
  resource rejection checks and trace redaction/exception-chain test pass.
  Embedded runtime classes match the compiled Java 17 classes. Four original SHA pins match.
- Existing unrelated edits, ControlledGenesis and authentication are untouched by this fix.

Evidence: `%LOCALAPPDATA%/Moonrise/dev/logs/legacy-weave-integration-20261008/`:
`bootstrap-final.txt`, `exit-final.txt`, `resource-checks.txt`, `trace-test.txt`,
`original-hashes.after.json`. Preparation (no app/game launch):
`legacy-weave-smoke-20261008-190435` (same logs root), settings/package backups included.

One manual command, from any directory:

```powershell
powershell -ExecutionPolicy Bypass -File "%WORKSPACE%\app\tools\Start-LegacyWeaveLoaderSmoke.ps1"
```

It selects Lunar/1.8.9, disables packages, opens the built Moonrise with startup
capture, and waits for the user to click Launch once. No extra diagnostic agent.
The mapping adapter is the required technical agent alongside the Weave agent.
Keep the console open; after checking the menu close Minecraft normally and
answer `y` only if the menu worked. `result.json` saves stages, observed JVM status,
exit code and user menu confirmation; `first-exception.txt` retains the first
failure and full captured trace. Moonrise's failure report prefers this captured
exception over a secondary window timeout. Raw captures are sanitized by the
existing developer native bridge. Live clean Lunar regression and Minecraft
menu remain unverified in this task because no game launch is authorized.

READY FOR MANUAL WEAVE SMOKE: YES.

---

# Legacy mapping adapter implementation — 2026-10-08

The completed runtime contract in `genesis-mapping-contract-manual-20261008-175315/contract.log`
supersedes the unresolved status in the earlier mapping investigation. No mapping audit repeated.

OfficialLunar's structured NativeBridgeLauncher path prepares the separate technical agent
only for Legacy Weave. Four original SHA-256 pins reject unsupported Lunar/Genesis,
Minecraft mapping distribution and Weave before starting the launcher. Clean launch is unchanged.
The existing launch UI restricts recovery to Minecraft 1.8.9; the adapter additionally checks
actual Genesis `ave.class` against the observed vanilla-obfuscated bytecode SHA-256.

The isolated agent uses the pinned Genesis native KIN reader and Lorenz XSRG writer.
It verifies semantic roundtrip including CL/MD/FD descriptors and direction, rejects
legacy descriptor-erasure conflicts, then writes one entry in a session-owned resource JAR:
`lunar/lunar_named_b5_1.8.9.xsrg`. Before WeaveMixinService is defined/constructed,
it rejects an existing resource, calls the proven public Genesis addURL, and checks
exactly one visible resource and byte equality through the actual loader. A transformer
failure halts the game JVM with exit 86 because Instrumentation would otherwise ignore it.
No original class bytes are modified. Both technical/resource JARs belong to existing
LaunchSession.Dispose cleanup, including startup failure cleanup.

Validation: Java 17 compilation; native-reader conversion and semantic roundtrip;
resource-only archive/path; test-loader pre-constructor visibility; conflicting resource
exit 86; 23 targeted .NET tests (adapter, launch plans, cleanup ownership); Release solution
build (SelfContained=false, UseAppHost=true), zero warnings/errors; git diff --check.
Four original hashes still match the contract. Test loader visibility is not a new actual
Genesis runtime test; that check executes during the pending manual smoke.

Manual smoke: preparation script now supports -PrepareOnly and uses the current Release
build. Attempt to prepare was blocked because Moonrise is already running; settings were
not changed and no application or Minecraft process was started. Close Moonrise manually,
then from app run:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Start-LegacyWeaveLoaderSmoke.ps1
```

This backs up only Moonrise package/settings metadata, disables all packages, verifies an
empty ~/.weave/mods and no inherited Java options, then opens Moonrise in the explicit
legacy loader smoke mode. Select OfficialLunar / Minecraft 1.8.9, confirm zero mods and
zero package Java agents, and manually click Launch. Mapping adapter is a hidden technical
dependency. Existing launch diagnostics and Lunar logs must be retained. Evidence required:
`[Moonrise mapping] XSRG FOUND... BEFORE WeaveMixinService construction`, successful
WeaveMixinService creation, active Mixin service, completed WeaveLoader.init, and observed
Minecraft main menu. A DEFINE message alone does not prove constructor/init completion.
On failure retain the first meaningful exception; do not fix another blocker here.
After exit verify launch-session cleanup. No runtime success is claimed.

READY FOR MANUAL WEAVE SMOKE: NO (configuration preparation blocked by running Moonrise).
READY FOR LEGACY WEAVE MOD SMOKE: NO.
