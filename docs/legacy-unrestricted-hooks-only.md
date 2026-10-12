# Original unrestricted / standalone legacy hooks — 2026-10-09

Offline contract verified; real Lunar/Minecraft smoke remains pending.

Original inspected read-only:
`private-assets/moonrise-user-backup-20260802-174244-pre-api-migration/unrestricted.jar`
(relative to the workspace parent of `app`). SHA-256 before/after:
`C9C9DC9E75E37291E2992CA7FBD0FF6EB851B811391D9C938DEC14AB73315C05`.
Its only metadata declaration is five hooks. No initializer or Mixin config is required.

The official loader 0.2.6 was downloaded into ignored `.work/legacy-unrestricted-audit`
and verified against the existing pinned hash `c4200d...0142b524`.
Upstream [WeaveLoader.kt](https://github.com/Weave-MC/Weave-Loader/blob/v0.2.6/src/main/kotlin/net/weavemc/loader/WeaveLoader.kt)
defaults entrypoints/mixinConfigs to empty lists and registers hooks independently.
[HookManager.kt](https://github.com/Weave-MC/Weave-Loader/blob/v0.2.6/src/main/kotlin/net/weavemc/loader/HookManager.kt)
dispatches empty-target hooks to all classes. The actual binary has `Hook()` and
`transform(org.objectweb.asm.tree.ClassNode, Hook.AssemblerConfig)`, matching this original.
No loader bytecode change is necessary for hooks-only loading.

`tools/legacy-mapping-probe/LegacyHooksProbe.java` decodes metadata using the actual
loader serializer, constructs and registers all five original hooks, calls every
hook, and dispatches a synthetic class through the actual HookManager. It also
confirms a positive Reach transformation (synthetic `3.0` constant becomes `300.0`).
The probe uses official commons-lang3 3.12.0 and the existing pinned Genesis JAR
for the loader's external MixinClassWriter dependency; neither is a custom adapter.
Compiled with `--release 17`, executed on JDK 25.0.3. This does not establish that
current Lunar target classes still match every hook's bytecode patterns.

The general resolver now preserves a single original legacy hooks-only mod for
Minecraft 1.8.9 only after original hash verification, a valid legacy launch plan,
and the existing pinned loader/Genesis/Lunar/mapping contract preflight. It uses
metadata and generation, with no new package-name or hash-specific exception.
Existing directory and mapping adapters remain the normal legacy pipeline.
Preflight itself creates no JARs. No new runtime JAR or unrestricted adapter exists.
`Prepare` rechecks the same runtime contract immediately before launch.

If the installed cached legacy loader is missing or a pinned runtime hash fails,
maintained resolution remains available and `LegacyPathBlocker` contains the
reason. The maintained unrestricted JAR and its fallback rule remain in place.
No package storage, settings, AppData, Lunar auth, or UI was changed during this task.

Multi-mod selections retain existing maintained resolution and generation checks.
There is no original-legacy unrestricted + current Veyra/BWH co-loading claim.
BWH's maintained hashes, rule, network technical agent, ExitLag bridge, and launch
ordering remain unchanged. Its initializer/Mixin metadata excludes the new path.

An isolated .NET harness with the real original and a workspace copy of the official
loader verified the production runtime contract against the existing Lunar binaries,
selected the original with zero replacements, and produced the existing directory
adapter → legacy loader plan. No game or initializer was launched. Evidence/work
files remain under ignored `.work/legacy-unrestricted-audit`.

## Minimal manual smoke

1. In the existing Moonrise library select only original unrestricted and Lunar
   Minecraft 1.8.9; disable other mods and package agents for this check. Do not run
   the zero-mod legacy-loader smoke mode. Leave the maintained JAR physically present.
2. Launch once manually. In the launch report confirm legacy generation/loader,
   original SHA above, and no unrestricted maintained replacement. If it resolves
   to maintained current instead, this is fallback, not a successful original smoke:
   check the cached 0.2.6 loader and pinned Lunar runtime contract before proceeding.
3. Confirm `[Weave] Loading ...` for the original and successful
   `[Weave] Initialized Weave` / `[Moonrise Weave] init-complete`. With this single
   hooks-only mod, completion implies its five constructors/registration returned;
   it does not prove target transformation or feature behavior.
4. In a local test world verify Reach display/range behavior. Check texture-pack
   overrides for Lunar assets, polling-rate behavior, staff-related option availability,
   and previously restricted/blacklisted mod settings where the client exposes them.
   Record each feature separately, including unavailable controls or unchanged behavior.
   Menu appearance alone is insufficient. No server interaction is needed.
5. Close normally; check temporary session cleanup and unchanged original SHA-256.
   Restore the prior package selection manually. BWH can be checked separately using
   its existing current maintained launch; do not include it in this legacy smoke.

Release build, targeted tests, and diff check are recorded in the task report.
No full suite, Minecraft launch, commit, push, merge, publish, or release was performed.
