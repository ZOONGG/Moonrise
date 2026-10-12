# Per-mod compatibility removal — 2026-10-09

## Result: blocked for original unrestricted; working resolution preserved

Veyra's original `77976039…` JAR is already selected unchanged on the general
Weave 1.3.4 path. There is no production Veyra replacement rule or name/hash
exception. The current synthetic adapter is shared loader infrastructure:
it repairs Genesis synthetic-class loading, resolver visibility and remapping;
it does not select or patch Veyra. Removing it would regress the current path.
Offline selection is verified; Minecraft runtime success is not claimed.

Original unrestricted `c9c9dc9e…` still requires maintained runtime `adb858f7…`.
The exact-hash rule remains intentionally intact. Removing it before implementing
and validating a general ABI bridge would lose the currently working resolution.
This task does **not** achieve original unrestricted execution on current Weave.

## Concrete blocker

Local `javap` inspection of original unrestricted and pinned Weave 1.3.4 proves:

- Legacy hooks extend `net.weavemc.loader.api.Hook` and invoke constructor `()V`.
- Their transform signature uses `org.objectweb.asm.tree.ClassNode` and legacy
  `Hook$AssemblerConfig`.
- Current loader expects `net.weavemc.api.Hook`, constructor `String[]`, shaded
  ASM `ClassNode` and current `Hook$AssemblerConfig`.
- Current `ModHook` derives a target set from `Hook.getTargets()` and maps those
  targets between namespaces. The original hooks use structural inspection of
  incoming classes; inventing an empty target set is not an equivalent bridge.

Metadata defaults or changing API-generation classification cannot resolve
these binary and dispatch differences. A general solution must prove metadata
registration, legacy Hook construction, ASM conversion or in-memory relocation,
AssemblerConfig behavior and structural hook dispatch in the same game loader.
There is no verified implementation of that contract in this workspace. The
separate legacy 0.2.6 path is not proof of compatibility with current mods.
No package-specific adapter or patched copy was introduced.

## Changes and retained files

The Compatibility Engine now validates `hooks` arrays and checks each declared
class exists, including packages with no entrypoint. It preserves generation
from class references and rejects legacy hooks with a current loader. This is
general validation, **not** implementation of the missing runtime ABI bridge.

Retained: unrestricted rule/build; BWH exact-hash rule and maintained runtime;
BWH ExitLag bridge, technical Java Agent and dependencies; shared current
synthetic and legacy infrastructure. No JARs were rewritten.

No files were deleted. `Start-VeyraSmoke.ps1` refers to an older explicit Veyra
build and is referenced by existing smoke/diagnostic documents; it is not a
launch-pipeline dependency, but its removal was not justified as unused.
Historical docs, test recovery fixtures, user AppData/packages, private originals
and saved smoke evidence were retained. No managed artifact was proven both
obsolete and unused. Catalog records outside the app repo were not modified.

## Validation

- Release solution build: passed, zero warnings/errors.
- 29 targeted tests: passed, zero skipped. Coverage includes production Veyra
  selection, retained exact-hash requirements, hooks-only inspection/immutability,
  invalid hooks, mixed-generation refusal, current synthetic infrastructure and
  BWH relay policies. Production-rule tests use synthetic package identities;
  they do not claim the real game loaded.
- Actual original JAR inspection: Veyra `77976039…` = current; unrestricted
  `c9c9dc9e…` = legacy; BWH `c39f6f1c…` = legacy. No inspection errors; before/after
  hashes match. Veyra actual production resolution selects the original path,
  zero maintained builds, API inspection reports Current.
- Nine protected file hashes match the task baseline: maintained-resolution
  source, three BWH services, embedded network-agent JAR, installed BWH runtime,
  installed unrestricted runtime and two installed Veyra JARs.
- `git diff --check`: passed. Existing uncommitted edits preserved.
- Minecraft, full suite, publish/release, push and merge were not run.

## Manual smoke through normal Moonrise Launch

1. Veyra only: enable original `77976039…`, disable other mods/package agents.
   Verify Weave 1.3.4, no maintained replacement and the shared synthetic adapter.
   Confirm initialization, mixins and representative UI/features in Minecraft;
   after exit verify original SHA-256 and normal session cleanup.
2. unrestricted: keep the current maintained resolution. With original `c9c9…`
   selected, verify the report identifies replacement `adb858…` explicitly;
   inspect hook effects and errors after launch. Do not label this an original
   runtime smoke or bypass the replacement to claim compatibility.
3. BWH regression: use the established baseline profile/packages. Original
   `c39f…` must resolve to the same `6da5…` runtime; already-maintained `6da5…`
   remains unchanged. Verify the existing ExitLag technical agent/bridge and
   BWH features/network requests. Repeat the previously working Veyra+BWH
   combination, inspect logs, then check original/runtime hashes and cleanup.

No account, auto-update, Cosmos/Cosmetics or UI changes were made by this task.
