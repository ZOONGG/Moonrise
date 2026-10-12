param([string]$JavaHome = 'C:/Program Files/Java/jdk-25.0.3', [switch]$PreparationOnly, [switch]$RequireDiscovery)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$current = Join-Path $env:LOCALAPPDATA 'Moonrise/dev/cache/runtime/weave/weave-loader.jar'
$legacy = Join-Path $env:LOCALAPPDATA 'Moonrise/dev/cache/runtime/weave/weave-loader-0.2.6.jar'
$raven = Join-Path $env:LOCALAPPDATA 'Moonrise/packages/weave/RavenWeave-1.1.4.jar'
$bwh = Join-Path $env:LOCALAPPDATA 'Moonrise/adapters/maintained-builds/6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57.jar'
$api = Join-Path $env:USERPROFILE '.weave/.maven-repository/net/weavemc/api/api-v1_8/1.3.4/api-v1_8-1.3.4.jar'
$output = Join-Path $root 'runtime/mixed-weave-poc/classes'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = Get-ChildItem "$PSScriptRoot/mixed-weave-poc" -Recurse -Filter *.java | ForEach-Object FullName
& "$JavaHome/bin/javac.exe" --release 17 -cp "$current;$legacy" -d $output $sources
if ($LASTEXITCODE) { throw 'Proof compilation failed' }
# Only current loader is on the JVM classpath. Facades and legacy API are selectively defined by the proof loader.
$proofOnly = Join-Path $root 'runtime/mixed-weave-poc/proof'
New-Item -ItemType Directory -Force -Path $proofOnly | Out-Null
Get-ChildItem $output -File | Copy-Item -Destination $proofOnly
$namedCache = Join-Path $env:USERPROFILE '.weave/.cache/jars/mcp-named_1.8.9'
$mappings = Join-Path $env:USERPROFILE '.weave/.cache/mappings/merged_1.8.9/mappings.tiny'
# Real 1.8.9 libraries already present in the local Minecraft cache; proof provider only.
$libraries = Join-Path $env:APPDATA '.minecraft/libraries'
$dependencies = @(
    'com/mojang/authlib/1.5.21/authlib-1.5.21.jar'
    'com/google/guava/guava/17.0/guava-17.0.jar'
    'io/netty/netty-all/4.0.23.Final/netty-all-4.0.23.Final.jar'
    'org/lwjgl/lwjgl/lwjgl/2.9.4-nightly-20150209/lwjgl-2.9.4-nightly-20150209.jar'
    'org/lwjgl/lwjgl/lwjgl_util/2.9.4-nightly-20150209/lwjgl_util-2.9.4-nightly-20150209.jar'
    'com/ibm/icu/icu4j-core-mojang/51.2/icu4j-core-mojang-51.2.jar'
) | ForEach-Object {
    $path = Join-Path $libraries $_
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing local dependency artifact: $_" }
    $path
}
& "$JavaHome/bin/java.exe" -Xverify:all "-Dmoonrise.proof.requireDiscovery=$($RequireDiscovery.IsPresent.ToString().ToLowerInvariant())" -cp "$proofOnly;$current" MixedWeaveProof $current $legacy $raven $output $bwh $api $namedCache $mappings $PreparationOnly.IsPresent @dependencies
if ($LASTEXITCODE) { throw 'Offline proof failed' }
