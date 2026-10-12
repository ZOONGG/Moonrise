# First legacy Stormy smoke — 2026-10-08

READY FOR MANUAL STORMY SMOKE: YES. Runtime result remains pending.

Selected existing dev-library JAR:
`%USERPROFILE%\AppData\Local\Moonrise\dev\packages\weave\Stormy-1.0.jar`
(212361 bytes), SHA-256
`52ed9da4c4f570fd43b32671e0d94fa5fc9e441168645d2e3de054169374b021`.
This is the local legacy binary with `me.tryfle.stormy.Main`, not the old
catalog patched binary with `StormyPreInitBridge` and hash `64a811...`.
Upstream provenance/version is not established by the filename. Metadata has no
declared version, dependency versions or Minecraft version. No binary was changed.

Metadata: `entrypoints=[me.tryfle.stormy.Main]`,
`hooks=[me.tryfle.stormy.hooks.CPSHook]`,
`mixinConfigs=[stormy.mixins.json]`. The required config contains ten mixins.
62 classes reference legacy `net/weavemc/loader/api/`, none reference current
`net/weavemc/api/`. All 127 classes have major version 61 (Java 17), despite
the config's `compatibilityLevel=JAVA_8`. The confirmed Java 17 baseline is appropriate.

Release build passed with zero warnings/errors; 20 focused tests passed for the
launch factory, mapping adapter, API inspector and mod-directory preparation.
Actual-JAR offline preparation passed: parser, Legacy02 detection, exact copy,
normal legacy factory, pinned mapping-resource preparation and MNR4 serialization.
Its agent order is the existing mapping-resource adapter, existing directory
adapter, untouched Weave 0.2.6. No package agents, network adapter or diagnostic
Java agent. No maintained compatibility-build rule matches this Stormy hash.

Offline evidence:
`%LOCALAPPDATA%/Moonrise/dev/logs/stormy-offline-e6a2ba9bc9ec4c56b67a04f3599594e8/`
contains metadata, class/API inspection, initializer/loader javap output,
`offline-result.json`, `mnr4-offline.txt`, and a small prepared session retained
as evidence. This was preparation only; neither a game entrypoint nor Stormy
initializer was executed. Existing unrelated uncommitted work is preserved.

Prepared settings/package backups and configuration:
`%LOCALAPPDATA%/Moonrise/dev/logs/legacy-stormy-smoke-20261008-221802-38b92e2627df4d68a48943df8ab34e5e/`.
Only Stormy is enabled; Lunar/1.8.9 is selected. The current source routes this
mod launch through OfficialLunar. ControlledGenesis and authentication are unchanged.

## Manual run

Close Moonrise first, then run:

```powershell
powershell -ExecutionPolicy Bypass -File "%WORKSPACE%\app\tools\Start-LegacyStormySmoke.ps1"
```

The script backs up settings/index again, selects only the exact-hash Stormy,
opens Moonrise and waits. Click Launch once. Do not enable additional packages.
The loader-only flag is deliberately absent: that mode rejects mods.
`--dev-weave-mod-smoke` only guards one legacy mod/zero package agents and adds
Mixin verbose/export properties to the existing plan. It changes no loader behavior.
The startup capture uses the same existing developer native bridge as the baseline.
Keep the console open, close Minecraft normally, then answer the menu question.

## Evidence gates

1. **Discovery:** `[Weave] Loading ...Stormy-1.0.jar` plus report/source/copy hash
   match and exactly one runtime mod. This alone proves no initializer execution.
2. **Initializer:** the pinned loader invokes all metadata entrypoints' `preInit`
   synchronously before `[Moonrise Weave] init-complete` / `[Weave] Initialized Weave`.
   With exactly this one mod and entrypoint, discovery plus successful completion
   proves `Main.preInit()` returned. This is an inference from inspected bytecode,
   recorded separately from detection. `preInit` subscribes to `StartGameEvent.Post`;
   later `dev.stormy.client.main.Stormy.init()` is a separate runtime gate. Do not
   equate successful event subscription with successful client initialization.
3. **Config registration:** pinned loader calls `Mixins.addConfiguration` for
   metadata configs before invoking entrypoints. Successful init with this detected
   JAR therefore proves registration. Verbose `Selecting/Preparing stormy.mixins.json`
   additionally proves Mixin selected/prepared it. Registration is not application.
4. **Application:** collect `Mixing <mixin> from stormy.mixins.json into <target>`
   lines, then inspect the corresponding exported target classes in `.mixin.out/class`.
   For example, exported Minecraft must contain Stormy's merged `clickMouseAfter`
   handler (possibly Mixin-renamed) and its call from the target method, with
   references to `Stormy.moduleManager` / `NoHitDelay`. Inspect with `javap -c -p`.
   A Mixing line alone is only processing evidence: a later injection can fail.
   Count unique Stormy mixins against all ten configured names; report each missing
   application as pending/skipped with evidence. Some targets need world entry.
   Confirm Stormy functionality separately; menu appearance alone is insufficient.

Export is enabled with decompilation disabled. Moonrise starts with the evidence
directory as its working directory, so check its `.mixin.out` first. Official Lunar
can choose a different game working directory; if exports are absent there, locate
the current run's `.mixin.out` under the actual game working directory before
claiming application. Copy relevant exports to evidence before closing if they
reside in a temporary session. Do not use exports left by an earlier attempt.

`result.json` distinguishes discovery, inferred preInit return/config registration,
config preparation, observed mixing lines, warning and JVM exit. It deliberately
does not infer application or later Stormy.init success. Raw sanitized JVM capture
is retained for bytecode/runtime review. `first-exception.txt` freezes the first
observed meaningful exception, observed stage and captured context; the full JVM
logs retain the continuing stack trace. If failure occurs, stop at that proven
stage and do not fix it in this task.

Treat `Some mixins may be skipped` / late initialization as an unresolved timing
warning even if the menu opens. Determine which targets were already defined and
which Stormy injections actually reached exported bytecode. No retransformation,
alternate loading path, special Stormy patch, or extra agent is introduced here.
After exit, verify unchanged Stormy hash, normal JVM exit, launch-session cleanup
and Moonrise report agent/package counts. Runtime compatibility is still untested.
