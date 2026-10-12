param([string]$JavaHome = 'C:/Program Files/Java/jdk-25.0.3')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'runtime/mixed-weave-adapter'
$current = Join-Path $env:LOCALAPPDATA 'Moonrise/dev/cache/runtime/weave/weave-loader.jar'
$legacy = Join-Path $env:LOCALAPPDATA 'Moonrise/dev/cache/runtime/weave/weave-loader-0.2.6.jar'
$raven = Join-Path $env:LOCALAPPDATA 'Moonrise/packages/weave/RavenWeave-1.1.4.jar'
if ((Get-FileHash -LiteralPath $current).Hash -ne '013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7') { throw 'Current loader hash changed.' }
$classes = Join-Path $build 'classes'
$facade = Join-Path $build 'facade/net/weavemc/loader/api'
New-Item -ItemType Directory -Force -Path $classes, (Join-Path $facade 'event') | Out-Null
# Reuse the tested ABI/event router, excluding every Minecraft/LWJGL fixture.
[IO.File]::WriteAllText((Join-Path $facade 'ModInitializer.java'), @'
package net.weavemc.loader.api;
public interface ModInitializer extends net.weavemc.api.ModInitializer {
    void preInit();
    @Override default void preInit(java.lang.instrument.Instrumentation instrumentation) {
        preInit();
        System.out.println("[Moonrise mixed] original legacy preInit returned: " + getClass().getName());
    }
}
'@)
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'mixed-weave-adapter') -Filter '*.java' | ForEach-Object FullName)
$sources += (Join-Path $facade 'ModInitializer.java')
& "$JavaHome/bin/javac.exe" --release 17 -cp "$current;$legacy" -d $classes $sources
if ($LASTEXITCODE) { throw 'Mixed adapter compilation failed.' }
& "$JavaHome/bin/java.exe" -Xverify:all -cp "$classes;$current" dev.moonrise.mixed.MixedTransformerTest
if ($LASTEXITCODE) { throw 'Mixed transformer regression failed.' }
& "$JavaHome/bin/java.exe" -Xverify:all -cp "$classes;$current" dev.moonrise.mixed.BuildMixedAdapter $legacy $raven $classes
if ($LASTEXITCODE) { throw 'Selective ABI extraction failed.' }
# Build tool is not runtime behavior and is excluded from the output archive.
$manifest = Join-Path $build 'manifest.mf'
[IO.File]::WriteAllText($manifest, "Manifest-Version: 1.0`r`nPremain-Class: dev.moonrise.mixed.MixedAgent`r`n`r`n")
$jar = Join-Path $build 'moonrise-mixed-raven-abi-0.1.0.jar'
& "$JavaHome/bin/jar.exe" --create --date=2026-10-11T00:00:00Z --file $jar -C $classes net/weavemc/loader/api -C $classes weave.mod.json
if ($LASTEXITCODE) { throw 'Mixed adapter packaging failed.' }
$agent = Join-Path $build 'moonrise-mixed-agent-0.1.0.jar'
& "$JavaHome/bin/jar.exe" --create --date=2026-10-11T00:00:00Z --file $agent --manifest $manifest -C $classes dev/moonrise/mixed/MixedAgent.class -C $classes dev/moonrise/mixed/MixedTransformer.class -C $classes dev/moonrise/mixed/MixedRuntime.class -C $classes moonrise-mixed
if ($LASTEXITCODE) { throw 'Mixed agent packaging failed.' }
Get-FileHash -LiteralPath $jar
Get-FileHash -LiteralPath $agent
