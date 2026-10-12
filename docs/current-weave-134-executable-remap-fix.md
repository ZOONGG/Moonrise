# Crash LCLU-GKSZXGBNITUM: executable synthetic remapping

Bundle: 20261008-234128. Attempt: 20261008173649522-1.
Game JVM 14280, parent 17680, start 2026-10-08 17:38:28.259 UTC
(23:38:28 Omsk), exit 1 at approximately 23:38:56. No game window.
The later minecraft-timeout is a monitoring result, not the Java root cause.

Fresh evidence directory:
%LOCALAPPDATA%/Moonrise/dev/logs/veyra-smoke-20261008-233636-9ae8db8dcc284dc8ad38815805120d9f

Launcher log in the bundle identifies JVM 14280 at 23:38:28.303.
Native log %LOCALAPPDATA%/Moonrise/logs/native-bridge.log lines 18918-18920
records startup-capture-attached, java-options-applied and child-bridge-ready
for 14280, all error=0. Native entries have no timestamps; correlation is by
PID and the matching capture files, not an invented native timestamp.
Weave attached 23:38:28, initialized 23:38:48, applied mixins 23:38:49-55.
Ichor messages are captured directly in this JVM's stdout.
current-linkage.log records pid=14280 at 17:38:28.913 UTC and the same Genesis
loader defining Args$1 at 17:38:55.292 UTC. Earlier exceptions are excluded.

## Actual build

Smoke script selected runtime/current-synthetic-linkage-build/Release/net8.0-windows/win-x64/Moonrise.exe.
Moonrise.dll timestamp 2026-10-08T17:27:44.9919020Z, SHA256
D41A637295D238DE9D38494A9D2C91D3DFE98ABDC50DFA6F0CDF0E9893233EF0.
Embedded adapter ID moonrise-current-weave-synthetic-1.0.0.
Extracted embedded SyntheticLoaderMethods matches the compiled corrected helper
byte-for-byte. Embedded resolver still invokes MappingsHandler.remap.
Runtime stdout lines 836-837 independently confirm the corrected resolution:
synthetic-resolver loader=Genesis requesting-loader=Genesis;
synthetic-generated ...Args$1 namespace=mcp-named.
Thus the prior SyntheticResolver linkage error is resolved in this real JVM.
The ephemeral adapter JAR was cleaned, so its complete archive hash cannot be
recovered; the build content and executed corrected path are both verified.
Weave 1.3.4 SHA256 013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7.

## Cause and fix

The first meaningful causal error, 23:38:55, is ClassFormatError:
Absent Code attribute in method that is not native or abstract in Args$1.
It is reported inside ExceptionInInitializerError; Reflector initialization,
NoClassDefFoundError and Failed to boot Minecraft are downstream failures.
Args$1 is now found and generated, but was not successfully defined.

javap of untouched Weave 1.3.4 shows MappingsHandler.remap calls
ClassReader.accept with flag 1 (SKIP_CODE). The adapter incorrectly used this
metadata remapper for a class it would define. This removed method bodies.
Offline actual Weave Args generation followed by the old production remap
reproduces the identical ClassFormatError, establishing causality.

Minimal generic fix: preserve all executable code and frames with ASM
ClassRemapper, ClassWriter(0), ClassReader.accept(..., 0), retaining Weave's
namespace mapper. No Veyra, Minecraft-class, or SHA-specific fix conditions.
Rebuilt embedded Java17 adapter bytes. No added diagnostic layer.

## Validation

Build-CurrentWeaveSyntheticAdapter.ps1 passed with -Xverify:all:
actual Weave 1.3.4 Args generator, old-remap exact-error reproduction,
production fixed remap, changed object descriptor with executable invocation,
actual adapted Genesis plus untouched Weave accessor transformation,
helper invisible to Genesis defining loader, game-loader definition and Args
invocation, duplicate/concurrent/unknown/delegation checks.
Release .NET build and 26 targeted tests passed (CurrentWeaveSyntheticAdapter,
CurrentLinkageDiagnostic, CurrentLaunchPlanFactory, WeaveLoaderCompatibility).
Java verification output: runtime/current-synthetic-adapter/verification.log.
No Minecraft launch or new smoke preparation, no publish/commit/push.
Original JARs, auth, ControlledGenesis, legacy Stormy, timeout/lifecycle and
pre-existing uncommitted work were not changed by this task.
Real Minecraft success remains to be verified manually.

READY FOR NEXT MANUAL SMOKE: YES

## Full fresh reported exception chain (stdout lines 849-890)

```text
[23:38:55] [Client thread/INFO]: [STDERR]: java.lang.IllegalStateException: Failed to boot Minecraft
[23:38:55] [Client thread/INFO]: [STDERR]: 	at com.moonsworth.lunar.genesis.ClientGameBootstrap.apply(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/jdk.internal.reflect.NativeMethodAccessorImpl.invoke0(Native Method)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/jdk.internal.reflect.NativeMethodAccessorImpl.invoke(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/jdk.internal.reflect.DelegatingMethodAccessorImpl.invoke(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.reflect.Method.invoke(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at com.moonsworth.lunar.genesis.CICOHIIRRHHIIHRROHOICOCRHHHHOO.CICOHIIRRHHIIHRROHOICOCRHHHHOO(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at com.moonsworth.lunar.genesis.Genesis.run(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at com.moonsworth.lunar.genesis.Genesis.main(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: Caused by: java.lang.reflect.InvocationTargetException
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/jdk.internal.reflect.NativeMethodAccessorImpl.invoke0(Native Method)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/jdk.internal.reflect.NativeMethodAccessorImpl.invoke(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/jdk.internal.reflect.DelegatingMethodAccessorImpl.invoke(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.reflect.Method.invoke(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at com.moonsworth.lunar.genesis.ClientGameBootstrap.apply(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	... 7 more
[23:38:55] [Client thread/INFO]: [STDERR]: Caused by: java.lang.NoClassDefFoundError: Could not initialize class net.optifine.reflect.Reflector
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.minecraft.crash.CrashReport.populateEnvironment(CrashReport.java:137)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.minecraft.crash.CrashReport.<init>(CrashReport.java:53)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.minecraft.crash.CrashReport.makeCrashReport(CrashReport.java:414)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.minecraft.client.Minecraft.run(SourceFile:312)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.minecraft.client.main.Main.main(SourceFile:124)
[23:38:55] [Client thread/INFO]: [STDERR]: 	... 12 more
[23:38:55] [Client thread/INFO]: [STDERR]: Caused by: java.lang.ExceptionInInitializerError: Exception java.lang.ClassFormatError: Absent Code attribute in method that is not native or abstract in class file net/weavemc/loader/impl/shaded/spongepowered/asm/synthetic/args/Args$1 [in thread "Client thread"]
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.ClassLoader.defineClass1(Native Method)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at com.moonsworth.lunar.genesis.ORCOIHCCICICIHRCCCCHHHRICHHICI.moonrise$resolveSynthetic(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at com.moonsworth.lunar.genesis.ORCOIHCCICICIHRCCCCHHHRICHHICI.moonrise$loadSynthetic(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at com.moonsworth.lunar.genesis.ORCOIHCCICICIHRCCCCHHHRICHHICI.loadClass(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.ClassLoader.loadClass(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.Class.getDeclaredFields0(Native Method)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.Class.privateGetDeclaredFields(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at java.base/java.lang.Class.getDeclaredFields(Unknown Source)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.optifine.reflect.FieldLocatorTypes.<init>(FieldLocatorTypes.java:25)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.optifine.reflect.Reflector.<clinit>(Reflector.java:379)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.optifine.reflect.ReflectorForge.<clinit>(ReflectorForge.java:32)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//Config.initGameSettings(Config.java:215)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.minecraft.client.settings.GameSettings.<init>(GameSettings.java:343)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.minecraft.client.Minecraft.startGame(SourceFile:349)
[23:38:55] [Client thread/INFO]: [STDERR]: 	at Genesis//net.minecraft.client.Minecraft.run(SourceFile:310)
[23:38:55] [Client thread/INFO]: [STDERR]: 	... 13 more
```
