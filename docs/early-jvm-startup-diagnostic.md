# Early JVM startup diagnostic (developer manual smoke)

Scope: OfficialLunar, Minecraft 1.8.9, Legacy Weave 0.2.6. No crash fix.
The previous PID 9808 / LCLU-AAUEKXZWIHYY has no premain-entry marker;
absence of an output resource JAR does not establish whether premain ran.
Do not infer an XSRG failure from those facts.

## Manual smoke

Close the existing Moonrise window. From the workspace run:

```powershell
./app/tools/Start-JvmStartupDiagnostic.ps1
```

This opens only Moonrise. It preserves the existing package/settings files and
passes both `--dev-legacy-weave-smoke` (the previous launch report has
`devLegacyLoaderSmoke=true`, zero mods/agents) and
`--dev-jvm-startup-diagnostic`. Confirm OfficialLunar / 1.8.9 and click Launch
once. Do not enable additional packages. The existing loader smoke checks still
reject enabled packages or JARs in `~/.weave/mods`.

Each invocation creates a fresh directory under
`%LOCALAPPDATA%/Moonrise/dev/logs/jvm-startup-*`. The process-local capture
environment variable is restored in the invoking shell after opening Moonrise.
There is no global environment change. `-PrepareOnly` creates evidence metadata
and copies the dev bridge without opening any application.

## Evidence

- `preparation.json`: preparation UTC, executable/dev bridge hashes, native log
  baseline byte offset; never an account snapshot or a command line.
- `bootstrap-*.json`: launch session identity, ordered agent IDs/hashes, manifest
  premain class, Java class major version 61, three bootstrap option fields,
  isolated loader parent/dependency contract. No authentication arguments.
- `jvm-<PID>-stdout.log` and `jvm-<PID>-stderr.log`: native CreateProcess pipe tee
  installed while the JVM is created suspended, before its main thread runs.
  Original OfficialLunar stdout/stderr pipe consumers remain connected.
  Lines are sanitized before persistence, flushed per line, final partial line
  flushed at EOF; at most 2 MiB per stream; oversized lines are omitted.
- `jvm-<PID>-mapping.log`: UTC/PID stages and sanitized complete exception chains,
  including suppressed causes and stack frames. Stage writes close/flush the
  writer immediately. It is outside session cleanup and exists only with the
  explicit developer capture environment variable.
- Native bridge events: use only bytes after `NativeLogStartOffset` in
  `%LOCALAPPDATA%/Moonrise/logs/native-bridge.log`, and match the new JVM PID.

Stages: premain entry (stderr marker before helper use), launch config read
begin/complete, isolated adapter class loading, KIN source open/entry uniqueness
verification, XSRG generation start, resource JAR creation begin/complete,
Genesis attachment begin/content verification, premain completion.
Conversion semantics, hashes and fail-closed behavior are unchanged.

The tee supports ordinary STARTUPINFO. An explicit extended startup attribute
list is preserved without replacement: it yields
`startup-capture-unsupported-extended-handles` for the child PID. File/thread
failures produce `startup-capture-file-failed` / `startup-capture-failed`.
If capture is unavailable, missing premain output is **not** evidence of a
pre-premain JVM failure. The real OfficialLunar path still requires manual
verification. Keep the official launcher open until the failed JVM exits and
pipes reach EOF; parent termination can interrupt capture workers.

If capture is attached and premain is absent, inspect the first JVM error in the
new PID files, then manifest, class major version/dependencies and generated
agent argument structure using the bootstrap metadata. If premain is present,
use the last reached stage and first underlying exception, retaining its outer
reflection wrapper and suppressed causes. Do not select old Lunar/Mixin logs by
mtime, and do not implement a fix during this diagnostic task.

## Validation performed

- Java `--release 17`; trace tests on Lunar's Java 17.0.18: reached-stage flush,
  complete causes/suppressed stack, secret redaction, stack frame preservation.
- Synthetic Java 17 `-javaagent` negative tests with `-version`, no game:
  invalid options enters premain; missing Premain-Class fails before premain.
- Native parser/pump tests: fragmented pipe lines, EOF partial line, redaction,
  executable-token detection; GCC x64 with warnings treated as errors.
- Release solution build, framework-dependent isolated output: 0 warnings/errors;
  targeted LegacyMappingResourceAdapter/NativeBridge tests: 23 passed.
- MSVC is unavailable locally. Dev DLL uses existing local GCC/MinHook; DLL
  imports are Windows/UCRT only. Production DLL and its pinned hash unchanged.
- Ordinary output EXE was locked by running Moonrise; successful build uses
  `runtime/startup-release/framework/Release/net8.0-windows/win-x64/`.
- No real Lunar/Minecraft launch performed. First meaningful exception and
  exact real failure point remain pending the user's manual smoke.

Rebuild .NET using:

```powershell
dotnet build app/Moonrise.sln -c Release --no-restore -p:SelfContained=false -p:BaseOutputPath=%WORKSPACE%/app/runtime/startup-release/framework/
dotnet test app/tests/Moonrise.Tests/Moonrise.Tests.csproj -c Release --no-build --no-restore -p:SelfContained=false -p:BaseOutputPath=%WORKSPACE%/app/runtime/startup-release/framework/ --filter 'FullyQualifiedName~LegacyMappingResourceAdapterTests|FullyQualifiedName~NativeBridge'
```

## Manual smoke result: 2026-10-08 18:30 Omsk (UTC+06)

Attempt `20261008123030166-1`, launch session
`launch-966e542cbe124f88804cf47f7f089840`, official launcher PID 13496,
**new game JVM PID 9524**. This is separate from previous PID 9808.
Evidence directory:
`%LOCALAPPDATA%/Moonrise/dev/logs/jvm-startup-20261008-183021-8d0aa7f5`.

Native events after preparation's byte offset 716317 confirm
`startup-capture-attached`, `java-options-applied`, `child-bridge-ready` for
PID 9524. Both streams were captured; stdout is empty, stderr contains the
agent exception. Mapping log confirms premain entry at
`2026-10-08T12:31:03.999998900Z` (18:31:03.9999989 Omsk).

Reached: config read, isolated adapter load, unique KIN entry, successful KIN
reader/namespace check, XSRG generation begin at 18:31:04.240309.

First meaningful underlying exception (outer wrappers: InvocationTargetException):

```text
java.util.ServiceConfigurationError: org.cadixdev.lorenz.io.MappingFormat: org.cadixdev.lorenz.io.srg.SrgMappingFormat not a subtype
```

Failure location: `MappingFormats.<clinit>` via ServiceLoader, triggered at
`LegacyMappingAdapter.convert(LegacyMappingAdapter.java:40)` by
`MappingFormats.XSRG.createWriter(text)`. Writer initialization fails before
XSRG output writing, before resource JAR creation, before Genesis resource
attachment. This is a provider/type compatibility error during service
loading; the specific defining/context loader mismatch has not been proved.
No converter semantic failure is established.

Moonrise reported `observedLaunchJava=False` and timed out at 18:34:02, but the
PID-specific native and agent logs prove the JVM existed and entered premain.
The timeout is not the original JVM failure. No runtime fix was implemented,
no new game launch was initiated, and no source code was changed for this
read-only evidence review. Prior passing diagnostic tests/build remain the
validation baseline.
