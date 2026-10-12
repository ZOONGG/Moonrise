# Local XSRG writer fix — 2026-10-08

PID 9524 evidence proves failure in MappingFormats static initialization, before
resource JAR creation. WriterIsolationTest reproduces the exact exception on the
pinned Genesis JAR with Lunar Java 17.0.18 and the adapter's platform-parent loader.
MappingFormat is defined by MoonriseLegacyMapping; the provider selected via the
unchanged application TCCL is defined by AppClassLoader. Both code sources are
`~/.lunarclient/offline/multiver/genesis-0.1.0-SNAPSHOT-all.jar` (SHA-256
`e3f34c4ad92f1c3bae83c17bbacb45952c156df359cb39d6c34d086b05e83c44`).
The isolated SrgMappingFormat is assignable to the isolated API; the application
copy is not. MappingFormats bytecode calls ServiceLoader.load(MappingFormat.class).
This is duplicate class identity across loaders, not evidence of incompatible
Lorenz versions. PID 9524's log did not record provider code sources; the loader
and code-source observations above are from the faithful offline reproduction.

MappingFormat, SrgMappingFormat, XSrgWriter/Reader and Bombe are bundled in that
Genesis fat JAR. Its manifest reports Implementation-Version 9.10.1 for these
packages; this is the aggregate manifest version, not a proven Lorenz/Bombe
dependency version. No per-dependency Lorenz/Bombe version metadata is present;
their exact upstream release versions cannot be established from this artifact.
The artifact hash and inspected APIs pin the actual runtime implementation.

The only conversion change is direct public XSrgWriter(Writer) and
XSrgReader(Reader) construction. Native KIN reading, namespace, descriptors,
roundtrip validation, conflict checks and resource attachment are unchanged.
The embedded adapter class was regenerated with javac --release 17.

Validation:

- Original ServiceConfigurationError reproduced, then conversion succeeds even
  with MappingFormats initialization poisoned; TCCL identity remains unchanged.
- Semantic roundtrip: 2,517 classes, 15,072 methods, 8,737 fields; CL/MD/FD syntax
  and representative Minecraft/getSessionInfo mappings checked.
- Lorenz emits 2,507 explicit CL lines; ten unchanged enclosing classes are
  reconstructed from inner mappings. Output is byte-identical to the earlier
  candidate: SHA-256 `ff4de06a88e87089a50643ed9ffe82ca3e3c44d4cd9dea9fb33fc326d6c25b88`.
- MappingAdapterTest passes with captured, hash-checked vanilla ave.class fixture:
  resource-only JAR and pre-constructor visibility through a test Genesis loader.
  An initial invocation with lunar.jar as the fixture correctly halted with
  `IllegalStateException: Minecraft 1.8.9 vanilla-obfuscated Genesis bytecode contract mismatch`;
  lunar.jar is not the captured vanilla fixture. No contract was relaxed.
- StartupTraceTest passes; 24 targeted .NET tests pass (adapter, current launch
  plans, cleanup ownership). Release solution build with SelfContained=false and
  UseAppHost=true passes, zero warnings/errors. No full suite or publish.
- Original Genesis, mapping, Lunar and Weave hashes still match their existing pins.

One manual smoke is prepared by Start-LegacyWeaveLoaderSmoke.ps1 -PrepareOnly.
Evidence: `%LOCALAPPDATA%/Moonrise/dev/logs/legacy-weave-smoke-20261008-184022`.
Moonrise settings/package metadata backups are there; all packages disabled;
legacy mods directory empty; no inherited Java option variables; loader 0.2.6 pinned.

Open `src/Moonrise/bin/Release/net8.0-windows/win-x64/Moonrise.exe` with argument
`--dev-legacy-weave-smoke`. Select OfficialLunar / Minecraft 1.8.9, check zero mods
and zero package agents, and click Launch once manually. Expect the mapping
resource verification message before WeaveMixinService construction; retain the
first new exception if any and stop investigation at that blocker. Verify session
cleanup after exit. No Minecraft process was started during this fix; real
Genesis/Mixin startup success remains untested.

READY FOR MANUAL SMOKE: YES.
