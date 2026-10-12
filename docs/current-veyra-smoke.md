# Current Weave / full Veyra manual smoke

Prepared 2026-10-08; Minecraft was not launched.

Latest correction: [resolver linkage fix](current-weave-134-resolver-linkage-fix.md).
The smoke script uses `runtime/current-synthetic-linkage-build`.
The older build paths below describe prior attempts.

Current prepared fix/smoke: [current-weave-134-synthetic-fix.md](current-weave-134-synthetic-fix.md).
The script now uses `runtime/current-synthetic-build` with the generic current
synthetic loading adapter. Full Minecraft behavior still requires manual smoke.

Latest failed-run analysis and diagnostic repeat: see
[current-weave-134-linkage-crash.md](current-weave-134-linkage-crash.md).
The prepared script now uses `runtime/current-linkage-build` and adds an explicitly
gated observational synthetic-class diagnostic. This is not a verified crash fix.

## Original and contract

Use `veyra/build/libs/Veyra-0.1.1-moonrise.1.jar`, SHA-256
`779760392bca1f269d0eb325e78950249d355e94a09c90988108c3ccd94d95bd`.
The imported dev-library `Veyra-0.jar` has the same bytes. The older
private-assets build lacks SpeedMine; the distinct osteli build is excluded.
No original JAR was rebuilt or changed.

The manifest declares no agent. Classfiles require Java 17. Metadata declares
current `net.weavemc.api` initializer `dev.veyra.weave.Main`,
`entryPoints`, `mixinConfigs`, `accessWideners`, a hook, and `mcp-named`.
There are no declared dependencies or compiledFor Minecraft target; source
build configuration targets 1.8.9 and compiles against Weave/API 1.3.4.
Mixin config is required, JAVA_8, with 17 listed Mixins including SpeedMine.
JAVA_8 in Mixin config does not lower the Java 17 classfile requirement.

Select the existing supported untouched Weave 1.3.4 agent, SHA-256
`013b7b9a4ca01a473a1ffc0b031c843c70fe61616e9048fb326e03794c8457b7`.
Do not select 0.2.6 or apply its mapping/service adapter to current Weave.
Production generation selection already distinguishes these APIs and rejects
mixed legacy/current sets. EnabledModDirectoryService stages unchanged bytes;
the native bridge supplies the session mods directory. Loader-owned remapping
and sandbox preparation are distinct from modifying originals.

## Generic changes

Compatibility Engine now retains current declarations and validates declared
initializer/resources and listed Mixin classes. Current API inspection consumes
these errors before launch. Legacy inspection/adapter behavior is unchanged.
The dev one-mod smoke flag now accepts either selected API generation and still
requires exactly one mod and no package agents. No mod-name/SHA/class exception
was added to runtime compatibility code.

The original 1.3.4 loader offline probe decodes the real metadata, discovers
exactly this JAR, reports sandbox PREINIT, and inserts one initializeMods call
into synthetic Minecraft main bytecode without executing it.
Its registerMod path consumes getMixinConfigs and populateMixinModifiers creates
namespace sandboxes before preInit. Its main hook dispatches init separately.
Veyra preInit installs its hook fallback; init subscribes StartGameEvent.Post,
whose callback calls Veyra.init. Menu or preInit return alone proves neither
client initialization nor feature operation.

`Some mixins may be skipped` is emitted by MixinBootstrap when initial phase is
DEFAULT. Current Weave's SandboxedMixinService returns PREINIT offline; it uses
its own relocated Mixin, unlike the legacy host-service integration. No phase
override or warning suppression was added. If the warning occurs live, identify
its emitting subsystem and review target bytecode before accepting the smoke.

## Validation and manual run

134 targeted tests passed: CompatibilityEngineTests, WeaveLoaderCompatibilityTests,
LegacyWeaveCompatibilityTests, CurrentLaunchPlanFactoryTests and
LegacyMappingResourceAdapterTests. Release build passed with zero warnings/errors.
Normal output was locked by the open Moonrise, so build output is under ignored
`runtime/veyra-smoke-build`. Offline production inspection/planning/staging of
the real Veyra passed: Current, one mod, one loader, identical staged/source hash.
The smoke script CheckOnly passed without changing settings or starting apps.

Close the currently open Moonrise, then from any directory:

```powershell
pwsh -NoProfile -File %WORKSPACE%\app\tools\Start-VeyraSmoke.ps1
```

The script opens only Moonrise; click Launch once yourself. It temporarily selects
only the exact full Veyra and disables Stormy/other mods and all package agents.
It retains package/settings backups, sanitized native JVM capture and Mixin debug
export settings. Close the game normally, then Moonrise, and answer the collector.
It records observed JVM exit code and restores package/settings bytes when
Moonrise is closed. If interrupted with Moonrise open, close it and restore the
two before.json backups explicitly. No Lunar account file is accessed by the tool.

Accept only with menu, successful initializer dispatch AND Veyra client startup,
actual required Mixin application, working Array List/Timer/SpeedMine in a local
world, functioning Lunar accounts and normal JVM exit. Inspect exported target
bytecode: Minecraft must contain calls to the injected timer update, and
PlayerControllerMP must contain the SpeedMine hardness redirects in both
clickBlock/onPlayerDamageBlock. Names may be remapped; merged metadata and call
owners provide evidence. Review other required Mixin targets too. Mixing log
lines show processing only. Functional observations corroborate bytecode evidence.
Runtime initialization, account functionality, Mixin application and JVM lifecycle
remain unverified until this manual run. Stormy's prior real baseline was not rerun.
