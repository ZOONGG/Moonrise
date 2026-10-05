# Compatibility Engine Stage 1

Design reference: [LAUNCH_ARCHITECTURE_RESEARCH.md](LAUNCH_ARCHITECTURE_RESEARCH.md).

The `Moonrise.Compatibility` namespace is an isolated planning API. It has no executor, UI registration, process creation, shell invocation, network access, or connection to Candidate #40 launch services. `Moonrise.Models.LaunchPlan` and its production callers are unchanged.

## Input and output contract

1. Use `ArtifactSnapshots.Capture(path, provenance)` to freeze an imported JAR's absolute canonical path, byte size, SHA-256, and read-only inspection. Package overrides live in `PackageInput.Override`, separately from detected requirements/evidence.
2. Discover runtime candidates with `JavaRuntimes.Discover(runtimeDirectory)` or inspect a custom Java executable/root. Only `bin/java[ w ].exe` existence and bounded `release` metadata are read. No JVM is executed.
3. Supply a `PlanRequest` from a versioned, verified contract. Use explicit immutable empty arrays for required collections. Classpath, Ichor inputs, natives, assets, entrypoint, agent constraints, and loader selection belong to that contract; there is no implicit Lunar layout discovery or universal Weave ordering.
4. `CompatibilityPlanner.Build` returns a deeply immutable plan, including errors and warnings. `Status` is `Valid`, `ValidWithWarnings`, or `Invalid`. Java executable/release files are fingerprinted and checked against discovery metadata. Disabled packages cannot enter agent slots.
5. A future executor must accept only a compatible frozen plan and call `ArtifactSnapshots.Validate(plan.ArtifactFingerprints)` immediately before use. Missing/changed bytes require rebuilding the plan. The executor must not rediscover dependencies or select another Java runtime behind the plan's identity.

The schema is version 1. `PlanId` hashes canonical JSON without its own ID, timestamps, or recognized secret values. `RuntimeSnapshotId` hashes ordered artifact paths/sizes/hashes. Collections that express JVM order preserve that order; package/fingerprint sets and diagnostics use ordinal stable ordering. Agent ordering uses a stable topological sort with user order and ID as tie breakers.

## Evidence and conservative limits

Startup agents require a manifest `Premain-Class` and its class entry. `Agent-Class` alone is attach-only. Manifest continuations and optional agent attributes are preserved. Agent options are independent structured values. A declared agent `Boot-Class-Path` needs a future verified dependency contract and is blocked in preflight.

Weave, Forge, and Fabric are identified by standard archive metadata markers. Multiple ecosystem markers require a type override; the override never clears invalid metadata, missing startup targets, Minecraft/Java/dependency constraints, or unverified loader requirements. No package name, filename, or artifact hash selects behavior.

Weave generation evidence comes from bounded class constant-pool class and descriptor references, not arbitrary UTF-8 strings or ZIP-wide text search. `net/weavemc/loader/api/` and `net/weavemc/api/` are independent evidence. `modId` does not identify generation; `compiledFor` describes Minecraft. Neither marker-only metadata nor mixed references silently select a loader. Unknown Weave generation blocks activation pending a verified contract. A loader generation needs matching inspection evidence or explicit `contract.weave.generation` evidence. Bundled selection additionally needs managed provenance (`Source` beginning with `managed`).

Inspection limits: 20,000 archive entries, 2 MiB per metadata/class entry, 4,096 classes, 32 MiB total inspected class bytes. Truncation and malformed class data remain unverified. Forge TOML supports ordinary quoted scalar and Boolean fields and standard dependency tables. Unsupported TOML constructs are reported, not guessed. Nested Fabric JAR declarations are retained but not recursively activated.

Version ranges support exact numeric versions, numeric comparisons, space-separated conjunctions, numeric prefix wildcards, and Maven-style intervals. Other expressions remain explicitly unverified and cannot satisfy a mandatory compatibility constraint. Forge/Fabric external-mod activation remains unsupported by the current Stage 1 contract, even when metadata and base loader match.

## Backend and argument policy

ControlledGenesis capabilities describe the future backend only. OfficialLunar exposes Java, classpaths, working/game directories, asset index, entrypoint, and final game arguments as delegated fields. Candidate inspection does not claim to identify OfficialLunar's eventual Java choice. Custom Java and managed memory/JVM arguments receive explicit unsupported errors; final agent ordering receives an unverified warning.

Auto Java selects compatible metadata candidates in stable order, considering contract bounds/architecture, classfile minimums (including standalone agents), and declared Java dependency ranges. It does not choose by directory age. Custom executable/root resolution never falls back to Auto. Unknown required version/architecture and stale release metadata are errors.

The argument parser supports single/double quotes and Windows paths, preserves literal backslashes, and handles runs before a matching quote with Windows escaping rules. It never evaluates a shell. Original user text is retained independently of accepted tokens and argument provenance.

Ordinary `-Dfoo=bar` properties are allowed. Managed property conflicts are errors; exact duplicates are removed with a warning and provenance. Memory aliases/percentages, classpath overrides, raw Java agents, entrypoint/module selectors, argfiles, native agents, and boot/module-path overrides are blocked. Memory min/max, conflicting user properties, and conflicting garbage collectors are checked. Unverified JVM switches produce warnings rather than compatibility claims.

Inherited JVM-option variables are represented in `ChildEnvironmentPolicy`; parsing and conflict evidence retain their source. Nonempty inherited values block the proposed managed launch until explicitly resolved. Global environment is never changed and values are never silently dropped.

`PlanSerialization.Diagnostics` produces a sanitized view with decision codes, sources, requirements, artifact evidence, and ordering. Sensitive property/flag values are redacted across issues and provenance as well as argument lists; nested JSON metadata is sanitized too. Account files and token-bearing process command lines are never read. Canonical diagnostics are not an executable transport: sanitized secret-bearing values cannot be reconstructed from them.

## Validation

Synthetic JARs and fake Java runtime directories are created only by tests and cleaned afterwards. Tests do not depend on the user's Lunar installation, run Minecraft, or make network requests.

Targeted command:

```powershell
dotnet test tests/Moonrise.Tests/Moonrise.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~CompatibilityEngineTests|FullyQualifiedName~UniversalLaunchPlanTests|FullyQualifiedName~CurrentLaunchPlanFactoryTests'
dotnet build Moonrise.sln -c Release --no-restore
git diff --check
```

Real Genesis contracts, authorization/ownership behavior, native preparation, integration smoke testing, and process execution belong to Stage 2. Unit tests establish the planning contract, not real Lunar/Minecraft compatibility.
