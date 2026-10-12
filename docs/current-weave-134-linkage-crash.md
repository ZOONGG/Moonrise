# Weave 1.3.4 linkage failure — 2026-10-08

Follow-up diagnostic run resolved the missing boundary; see
[current-weave-134-synthetic-fix.md](current-weave-134-synthetic-fix.md).
The observations below describe the earlier evidence, before that run.

Status: failure mechanism identified; integration root cause not yet proven.
No speculative runtime repair was applied. One diagnostic manual smoke is ready.

## Same-attempt correlation

Branch `codex/controlledgenesis-stage2`; pre-existing dirty changes retained.
Attempt `20261008164713851-1`, session `launch-d7934cf6a4f148e8a217c21a418ee52b`.
Times below are Asia/Omsk (UTC+06:00), 2026-10-08.

- Bundle: `%LOCALAPPDATA%/Moonrise/dev/logs/crashes/20261008-225041`.
- Moonrise: `moonrise-20261008-224655-801.log`, same data-root logs directory.
  Java PID 20832, parent official launcher PID 21140; JVM start
  `2026-10-08T16:47:39.3369124Z`, exit 1 observed 22:48:12.
- JVM: `veyra-smoke-20261008-224653-34b52868e6d349ad88b8ff28344aa944/jvm-20832-stdout.log`
  and `jvm-20832-stderr.log` under that logs directory. Full stdout retains
  earlier bootstrap lines omitted by the bundle's bounded tail.
- Native: `%LOCALAPPDATA%/Moonrise/logs/native-bridge.log`, final matching
  PID 20832 segment: `startup-capture-attached`, `java-options-applied`,
  `child-bridge-ready`, all error=0. Older PID reuse exists; PID alone is
  insufficient. Match the capture segment and JVM capture filename too.
- Lunar: bundle `recent-lunar-log.txt`: 22:48:10.537 reports
  `[UNKNOWN/EXIT_CODE_1] Unknown Java Crash` and collects the profile logs.
- Ichor: `%USERPROFILE%/.lunarclient/profiles/1.8/logs/ichor-boot.log`, starts
  22:47:40, ends 22:48:05; profile `latest.log` ends 22:48:09 and repeats
  the same fatal exception chain. Their timestamps/content match this attempt.
- Weave: bundle `recent-weave-log.txt` preserves the matching 22:47:39–22:48:09
  interval of `%USERPROFILE%/.weave/logs/latest.log`.

Crash ID `LCLU-ADAKSQXJSVIE` is user-observed; Moonrise bundle reports
`crashId=not-found`. Do not claim the bundle independently captured that ID.

## First meaningful exception and stage

At 22:48:09, full JVM stdout line 870 / bundle recent-java-output line 590:

```text
java.lang.NoClassDefFoundError:
net/weavemc/loader/impl/shaded/spongepowered/asm/synthetic/args/Args$1
```

It is retained as the original exception inside `ExceptionInInitializerError`.
Original stack: `Class.getDeclaredFields0` → OptiFine `FieldLocatorTypes:25`
→ `Reflector.<clinit>:379` → `ReflectorForge.<clinit>:32` → `Config.initGameSettings:215`
→ `GameSettings.<init>:343` → `Minecraft.startGame:349` → `Minecraft.run:310`.
Crash reporting subsequently encounters `Could not initialize class Reflector`;
`IllegalStateException: Failed to boot Minecraft` is an outer wrapper.

Stage is Minecraft runtime startup/linkage after Mixin processing, not premain
or Weave bootstrap. Weave attached 22:47:39, initialized 22:48:00, prepared 17
configured Mixins and processed targets through 22:48:09. The JAVA_8/classfile-61
warning is not the recorded causal exception. Logs do not prove successful
completion of every initializer or application of every required Mixin.
The later 22:50:39 `minecraft-timeout` classification is secondary and unchanged.

## Proven boundary and missing evidence

The game loader cannot resolve a relocated synthetic Args class while OptiFine
reflects over a target's fields. Existing logs do not show the synthetic class's
owning namespace sandbox, registry contents, resource publication, or definition.
They cannot distinguish failed generation/publication from wrong-loader resolution
or an incorrectly remapped host reference. Therefore a general pipeline repair
is not yet justified.

Local inspection of the untouched 1.3.4 bytecode confirms separate namespace
`mixinInstances`, per-sandbox `SyntheticClassRegistry.classes`, and injected resource
maps. The new optional Java-17 diagnostic records those names/maps immediately
before `Reflector` definition, relevant loader identities, incoming synthetic
references, and any actual synthetic class definitions. Incoming bytes are observed
at this transformer's position, not guaranteed final post-Weave bytes. No class is
generated, defined, retransformed or repaired; reflection failures are recorded.
No initializer, auth method, game main or synthetic generation API is called.
Log is bounded to 600 lines and contains class/resource names only.

Diagnostic is gated by Current mode, explicit `--dev-jvm-startup-diagnostic`,
and `MOONRISE_CURRENT_LINKAGE_AGENT`. Legacy Stormy and clean launch plans remain
identical; production launches without this opt-in remain identical.
No Veyra/mod-name/hash condition was added to runtime integration code.

## Validation and one manual repeat

- 156 targeted .NET tests passed, including five diagnostic isolation/plan tests,
  current and legacy compatibility, original launch planning, mapping adapter and
  native bridge configuration/deployment tests.
- Java offline diagnostic tests passed: observational transform, reference capture,
  output bound and actual 1.3.4 reflection field contracts.
- Original current-loader probe passed discovery=1, sandbox PREINIT,
  initializer dispatch=1 without executing the initializer or game main.
- `Start-VeyraSmoke.ps1 -CheckOnly`, repository safety and diff whitespace passed.
- Release solution build: zero warnings/errors; prepared app under ignored
  `runtime/current-linkage-build/Release/net8.0-windows/win-x64`.
  Earlier output was locked by open Moonrise; no process was stopped.
  Tests use a framework-dependent output because a self-contained app's adjacent
  hostfxr made testhost search that directory for missing shared frameworks.
- Veyra SHA-256 remains `779760392BCA1F269D0EB325E78950249D355E94A09C90988108C3CCD94D95BD`.
  Current Weave remains `013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7`;
  legacy 0.2.6 and Genesis hashes also match the existing pinned originals.

Close the existing Moonrise, then run:

```powershell
pwsh -NoProfile -File %WORKSPACE%\app\tools\Start-VeyraSmoke.ps1
```

The script opens Moonrise only; click Launch once manually. Preserve
`current-linkage.log` alongside JVM captures from the printed evidence directory.
After normal game/Moonrise closure the collector restores package/settings bytes.
The next analysis must compare registered Args names, sandbox/resource ownership
and observed definitions against the failed name before choosing a repair.
Minecraft was not launched during this task. No commit, merge, release, UI,
ControlledGenesis, timeout UX or authentication change was made.

READY FOR MANUAL VEYRA SMOKE: YES (diagnostic repeat; no runtime-fix claim).
