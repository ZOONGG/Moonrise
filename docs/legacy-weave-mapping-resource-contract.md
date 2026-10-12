# Legacy Weave mapping-resource investigation — 2026-10-08

Status: **BLOCKED before adapter implementation**. Binary conversion is proven;
the current Genesis resource namespace/bytecode contract is not. No mapping was
installed and no additional game launch was performed. This does not establish
that a compatible adapter is impossible.

## Inputs and provenance

Continues `legacy-weave-mixin-service-diagnosis.md`; its actual Genesis loader,
provider constructor failure, and pinned Weave v0.2.6 sources were reused.
The required resource originates from Weave's hard-coded constructor dependency
on Lunar's legacy mapping distribution, not a mapping bundled in Weave. An
original historical XSRG and its distribution revision were not found locally.
The candidate comes from the **currently installed** Lunar mapping JAR, not an
external download or a guessed replacement.

SHA-256 pins:

| Artifact | SHA-256 |
| --- | --- |
| Weave 0.2.6 | `c4200d145c0d3d78f57eab4ddee3a64ff1a3176e65f2e2b15a31777a0142b524` |
| Genesis | `e3f34c4ad92f1c3bae83c17bbacb45952c156df359cb39d6c34d086b05e83c44` |
| lunar.jar | `bb37980cdf62e4576adcd700268dc24359a7d7ca48090b9b698696df9d89932f` |
| lunar-platform-mappings-v1_8.jar | `5d53fd9767c085475ad139fd7e667a28cb3c0fedaf526b495efc8fe515add521` |

Search inspected ZIP entry names in 127 JARs under `app/runtime`, `app/.work`,
Lunar `offline` / `mx-cache`, and installed/development Moonrise `cache/runtime`.
No `.xsrg` entries or loose mappings were found in those scopes. This is not a
claim about every file on the machine or historical artifacts available online.
Exact-name web searches also returned no results; they establish no upstream
availability claim.

## Format and legacy semantics proven

The Genesis KIN MappingFormat implementation identifies the `kin` extension and
creates its own binary reader:

`com.moonsworth.lunar.ICCRRCCIOOIRICIHHCIOROHHROOHHR.RCHICCCIHRCRCHCIRRIIRRHICORHHO.IHIRRHORRRRHICCOHOHORCHIHHHIOO`.

Its bytecode establishes big-endian Java DataInputStream encoding: magic
`0x05E8F046`, version byte `1`, package-pair count and UTF pairs, top-level class
count, then recursive class records. A class has source/target names, inner
classes, fields (source name, source field type, target name), and methods
(source name, source descriptor, target name). UTF uses Java modified UTF-8.
The reader consumes package pairs without adding them to MappingSet; the
candidate's package-pair count is zero.

The offline probe uses that exact native reader and Genesis's Lorenz XSRG writer
and reader. For `lunar/lunar_named_b5_1.8.9.kin` it verifies complete semantic
equality after KIN → XSRG → MappingSet, including destination descriptors and
inner-class names:

- 2,517 classes;
- 15,072 methods;
- 8,737 fields;
- legacy `CL:` (2 values), `MD:` / `FD:` (4 values) syntax;
- every class/member source key and destination name under the legacy parser's
  lookup rules, including method source descriptors and implicit identity classes;
- no conflicting field destinations when the legacy parser erases descriptors.

The independent legacy-record check models the pinned XSrgMapper lookup rules.
It is not execution of XSrgMapper, WeaveMixinService, or the actual Genesis
classloader. `candidate.xsrg` is a genuine offline conversion saved as evidence
only; it is not registered as a runtime resource.

## Unresolved runtime contract

The candidate's direction is **vanilla obfuscated → MCP named**:

- `a` → `net/minecraft/util/EnumChatFormatting`;
- `ave` → `net/minecraft/client/Minecraft`;
- `ave/ak ()Ljava/util/Map;` →
  `net/minecraft/client/Minecraft/getSessionInfo ()Ljava/util/Map;`.

Current `lunar2searge` uses Lunar named → SRG, for example
`net/minecraft/client/Minecraft/loadWorld
(Lnet/minecraft/client/multiplayer/WorldClient;Ljava/lang/String;)V` → `func_71353_a`.
The inflight mappings also use named classes, with version-suffixed source
members such as `setInitialDisplayMode$v1_8` → `setInitialDisplayMode`.

Direct candidate source-key overlap with `lunar2searge` is only 5/2,517 classes,
0/15,072 methods and 0/8,737 fields. **This is not a runtime incompatibility proof**:
Genesis `findResource` expands `.class` names through the Ichor mapping resolver;
`getResourceAsStream` also passes bytes through the pipeline. Raw archive entry
names or mapping-set intersections cannot establish the returned bytecode's
namespace. The existing diagnostic does not capture vanilla-obfuscated class
resource lookups or the returned class/member names and descriptors.

Weave `getClassNode` reverses the candidate's class mapping, requests e.g.
`ave.class`, and applies the same mapper to the returned bytes. The missing proof
is that the **actual defining Genesis loader**, at that bootstrap stage, accepts
those legacy keys and supplies class/member/descriptors consistent with this
mapping. Alternatively, a complete, validated namespace composition would be
needed. Neither has been established by this investigation. A different named
mapping or an inferred identity mapping was not substituted.

Smallest next diagnostic: a targeted read-only probe at the actual Genesis
boundary that checks legacy source resource aliases and returned class/member
descriptors against the candidate, before any Mixin service construction. Merely
adding the converted resource and seeing the constructor parse it would not
validate that contract.

## Stop condition and validation

Per the requested stop condition, no `LegacyMappingResourceAdapter`, launch
integration, preflight implementation, or adapter JAR was added. The original
missing-resource NPE remains unresolved. There is no new game/runtime exception:
no additional smoke was attempted before establishing a verified fix.

WeaveMixinService creation, Mixin service selection, WeaveLoader.init completion,
actual Genesis resource visibility and the Minecraft menu remain unverified for
an adapter. Missing/mismatched-adapter tests are likewise not applicable to an
unimplemented adapter; this probe does not improve launch-time error handling.

Evidence: `%LOCALAPPDATA%/Moonrise/dev/logs/legacy-mapping-resource-20261008/`:
`probe.txt`, `candidate.xsrg`, `kin-reader.javap.txt`,
`local-artifact-search.json`, `original-hashes.before.json`,
`original-hashes.after.json`. The four original artifacts rehashed identically.

Validation: offline native-reader conversion, complete XSRG semantic round trip,
legacy record semantics and field descriptor-erasure checks passed; Java compiled
with `javac --release 17`; Release solution build passed (0 warnings/errors) with
`SelfContained=false` / `UseAppHost=false`; `git diff --check` passed. No full suite,
merge, release, production code changes, or original JAR modification.

Reproduce after checking the SHA-256 pins above (JDK path is local tooling):

```powershell
$genesis = "$env:USERPROFILE/.lunarclient/offline/multiver/genesis-0.1.0-SNAPSHOT-all.jar"
$mapping = "$env:USERPROFILE/.lunarclient/offline/multiver/lunar-platform-mappings-v1_8.jar"
$evidence = "$env:LOCALAPPDATA/Moonrise/dev/logs/legacy-mapping-resource-20261008"
New-Item -ItemType Directory -Force .work/legacy-mapping-probe, $evidence | Out-Null
& 'C:/Program Files/Java/jdk-25.0.3/bin/javac.exe' --release 17 -cp $genesis -d .work/legacy-mapping-probe tools/legacy-mapping-probe/LegacyMappingProbe.java
& 'C:/Program Files/Java/jdk-25.0.3/bin/java.exe' -cp ".work/legacy-mapping-probe;$genesis" LegacyMappingProbe $mapping "$evidence/candidate.xsrg"
```

READY FOR LEGACY WEAVE MOD SMOKE: **NO**.
