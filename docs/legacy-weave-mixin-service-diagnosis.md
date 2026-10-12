# Legacy Weave 0.2.6: Mixin discovery diagnosis (2026-10-08)

The required Weave provider is present and type-compatible with Genesis's Mixin service API, but its constructor fails because the legacy mapping resource `lunar/lunar_named_b5_1.8.9.xsrg` is absent. Current Lunar ships `lunar/lunar_named_b5_1.8.9.kin`. This is the proven blocker for selecting `WeaveMixinService`; full compatibility after removing that blocker has not been established.

## Evidence

Baseline: `%LOCALAPPDATA%/Moonrise/dev/logs/legacy-weave-smoke-20261008-165648/RESULT.md`.

Actual diagnostic run: `%LOCALAPPDATA%/Moonrise/dev/logs/mixin-service-diagnostic-20261008-171912/`:

- `discovery.log`: actual resource enumeration, defining loaders, code sources, all three complete exception chains.
- `weave-bootstrap.sanitized.log`: same final failure as baseline, `INVALID[LaunchWrapper] and 3 other invalid services`.
- `moonrise-launch.json`: OfficialLunar/MNR4, zero mods and package agents, diagnostic agent followed by untouched Weave.
- `original-hashes.before.json` / `original-hashes.after.json`: Weave and Genesis unchanged.
- `jar-resources.json`: current local JAR descriptors, mapping resources, hashes.
- `ichor-service.javap.txt`, `ichor-bytecode-provider.javap.txt`, `genesis-classloader.javap.txt`: actual runtime bytecode contracts.
- `upstream-*`: pinned upstream source copies.

Weave SHA-256 remains `c4200d145c0d3d78f57eab4ddee3a64ff1a3176e65f2e2b15a31777a0142b524`.
Genesis SHA-256 remains `e3f34c4ad92f1c3bae83c17bbacb45952c156df359cb39d6c34d086b05e83c44`.

## The three discarded provider exceptions

Each begins with `java.util.ServiceConfigurationError: org.spongepowered.asm.service.IMixinService: Provider <name> could not be instantiated`. These are caught and discarded, not attached using `Throwable.addSuppressed`.

1. `com.moonsworth.lunar.ichor.IHIRRHORRRRHICCOHOHORCHIHHHIOO.CICOHIIRRHHIIHRROHOICOCRHHHHOO.ORCOIHCCICICIHRCCCCHHHRICHHICI` (Ichor): `NullPointerException`, because the pipeline argument is null when the bytecode-provider constructor calls `HRIRHCHCCOOHRIHRRHOOOHICHCRHOC()`. The service constructor obtains that argument from its defining loader's `ROOCIHHOCOIHCCORHRIRHRRICOHCRO()` getter. Genesis implements the required Ichor API interface, so its type check succeeds, but its pipeline field is not assigned at this point. This is an actual context/initialization boundary, not an incompatible provider subtype.
2. `org.spongepowered.asm.service.modlauncher.MixinServiceModLauncher`: `NoClassDefFoundError: cpw/mods/modlauncher/api/IEnvironment$Keys` -> `ClassNotFoundException: Failed to find class cpw.mods.modlauncher.api.IEnvironment$Keys` -> `ClassNotFoundException: IchorPipeline can't find class in Genesis: cpw.mods.modlauncher.api.IEnvironment$Keys`. The provider constructor calls `getModLauncherApiVersion`; the ModLauncher API is unavailable in this runtime.
3. `net.weavemc.loader.mixins.WeaveMixinService`: `NullPointerException` at `XSrgMapper.<init>(XSrgMapper.kt:11)`, called by `WeaveMixinService.<init>(WeaveMixinService.kt:43)`. Its mandatory `/lunar/lunar_named_b5_1.8.9.xsrg` stream is null; Kotlin's `!!` throws before `isValid()` can run. The live Genesis resource enumeration reports zero matching resources. The actual bytecode and pinned source agree on the resource and null assertion.

The remaining discovered provider, `org.spongepowered.asm.service.mojang.MixinServiceLaunchWrapper`, constructs successfully but returns `isValid() == false`. LaunchWrapper is unavailable. It is the named `INVALID[LaunchWrapper]`, not one of the three constructor errors.

## Descriptors, classes, and boundaries

Genesis exposes its descriptor listing Ichor, LaunchWrapper, ModLauncher, plus the untouched Weave descriptor listing Weave. The Weave descriptor URL appears twice in resource enumeration; Java ServiceLoader deduplicates the provider name. There are four distinct providers and one Weave constructor failure, not two Weave initializations established by that duplicate URL.

Actual defining-loader chain:

```text
Genesis ORCOIHCCICICIHRCCCCHHHRICHHICI@724bade8
  -> Bootstrap#Game CICOHIIRRHHIIHRROHOICOCRHHHHOO@1530c739
     -> platform
```

TCCL is the same Genesis instance. `MixinService.initService()` explicitly uses its own defining loader for discovery, so changing TCCL alone would not change this lookup.

`MixinBootstrap`, `MixinService`, `IMixinService`, all four provider classes, and `XSrgMapper` are defined by that same Genesis instance. Mixin classes and the three host providers come from the Genesis JAR; Weave classes come from the original Weave JAR. The bootstrap SPI `IMixinServiceBootstrap` is separately observed in `Bootstrap#Game`; this does not produce a provider subtype failure in the captured `IMixinService` discovery.

There is no observed `not a subtype`, `ClassCastException`, duplicate `IMixinService` definition, or alternate Mixin implementation supplying the failing discovery API. ReplayMod contains another descriptor on disk, but that descriptor is absent from this live discovery enumeration. Its presence on disk alone is not evidence of a runtime conflict.

Genesis contains a Mixin `0.8.7` version marker. Weave was compiled against Mixin `0.8.5` as `compileOnly` and does not bundle those Mixin classes. Version difference exists; an API incompatibility is not the observed cause of these failures.

## Pinned upstream contract

Tag `v0.2.6` resolves to commit `1fa9afe9da187efe8694e881c8547ded982b25a2` (GPL-3.0).

- [Agent.kt](https://github.com/Weave-MC/Weave-Loader/blob/1fa9afe9da187efe8694e881c8547ded982b25a2/src/main/kotlin/net/weavemc/loader/bootstrap/Agent.kt): initializes Weave using the loader defining the first `net/minecraft/client/` class, adding its JAR URL first. This observed run enters through `ClientGameBootstrap.apply` in Genesis.
- [WeaveLoader.kt](https://github.com/Weave-MC/Weave-Loader/blob/1fa9afe9da187efe8694e881c8547ded982b25a2/src/main/kotlin/net/weavemc/loader/WeaveLoader.kt): boots Mixin, then requires the selected service to be `WeaveMixinService`, before loading any mods.
- [WeaveMixinService.kt](https://github.com/Weave-MC/Weave-Loader/blob/1fa9afe9da187efe8694e881c8547ded982b25a2/src/main/kotlin/net/weavemc/loader/mixins/WeaveMixinService.kt): constructor eagerly builds the legacy mapper; `isValid()` always returns true.
- [XSrgMapper.kt](https://github.com/Weave-MC/Weave-Loader/blob/1fa9afe9da187efe8694e881c8547ded982b25a2/src/main/kotlin/net/weavemc/loader/mappings/XSrgMapper.kt): requires a non-null text XSRG resource.
- [build.gradle.kts](https://github.com/Weave-MC/Weave-Loader/blob/1fa9afe9da187efe8694e881c8547ded982b25a2/build.gradle.kts) and [versions](https://github.com/Weave-MC/Weave-Loader/blob/1fa9afe9da187efe8694e881c8547ded982b25a2/gradle/libs.versions.toml): Mixin 0.8.5, compile-only; ASM is bundled.

## Conclusion and smallest correction to investigate

The first exception chronologically during provider construction is the Ichor null-pipeline error. The decisive blocker for Weave's required service is the missing legacy XSRG mapping resource. Delaying initialization until Ichor becomes available is not established as a correction: selecting Ichor would violate Weave's explicit service check, and it would not create the missing mapping resource.

The smallest generic compatibility change suggested by this evidence is a separate, versioned mapping-resource adapter that exposes a correct legacy XSRG representation of the current Lunar mapping to the loader defining `XSrgMapper`. A real `.kin` -> legacy-XSRG conversion with matching namespaces/schema must be verified first. Renaming a binary `.kin`, supplying an empty mapping, disabling validation, forcing `mixin.service`, or rearranging agents is not a demonstrated fix.

No compatibility fix was implemented. No further diagnostic smoke is needed to establish these three causes. A smoke after implementing a genuine mapping adapter would be a compatibility validation, with possible later failures still untested.

## Diagnostic changes and validation

Only explicit dev-smoke wiring and a separate diagnostic tool were added. The tool observes definitions/resources and prints the throwable inside the existing in-memory discovery catch; original iteration, constructors, selection and validation remain unchanged. It uses isolated ASM and no game-loader helper classes. The first additional launch through `JAVA_TOOL_OPTIONS` repeated the failure but produced no diagnostic premain marker; the structured MNR4 launch captured the evidence above.

Release build passed. All 13 `CurrentLaunchPlanFactoryTests` passed using framework-dependent test output; the first test invocation was aborted by testhost runtime resolution and was rerun with `SelfContained=false` / `UseAppHost=false`. Java catch instrumentation passed an offline probe on Lunar's Java 17.0.18 and the actual OfficialLunar run. `git diff --check` passed. Existing unrelated local changes were preserved. No merge, deployment, release, auth/UI/backend change, or third-party JAR modification was performed.
