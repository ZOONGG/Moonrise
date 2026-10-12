$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
$build = Join-Path $appRoot '.work/mixin-service-diagnostic'
$javaHome = 'C:/Program Files/Java/jdk-25.0.3'
$data = Join-Path $env:LOCALAPPDATA 'Moonrise/dev'
$loader = Join-Path $data 'cache/runtime/weave/weave-loader-0.2.6.jar'
$exe = Join-Path $build 'build/Moonrise.exe'
if (Get-Process Moonrise,java,javaw -ErrorAction SilentlyContinue) {
    throw 'Close Moonrise and existing JVMs before this isolated diagnostic.'
}
foreach ($name in @('JAVA_TOOL_OPTIONS', '_JAVA_OPTIONS', 'JDK_JAVA_OPTIONS')) {
    if ([Environment]::GetEnvironmentVariable($name)) { throw "Inherited $name invalidates isolation." }
}
if ((Get-FileHash -LiteralPath $loader).Hash -ne 'C4200D145C0D3D78F57EAB4DDEE3A64FF1A3176E65F2E2B15A31777A0142B524') {
    throw 'Untouched loader hash mismatch.'
}
$index = Get-Content -LiteralPath (Join-Path $data 'packages/metadata/index.json') -Raw | ConvertFrom-Json
if (@($index.Packages | Where-Object Enabled).Count -ne 0) { throw 'Disable all packages first.' }
if (Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.weave/mods') -Filter '*.jar' -File -Recurse -ErrorAction SilentlyContinue) {
    throw 'Legacy ~/.weave/mods must be empty.'
}
New-Item -ItemType Directory -Force (Join-Path $build 'classes') | Out-Null
& "$javaHome/bin/javac.exe" --release 17 -cp $loader -d "$build/classes" "$PSScriptRoot/mixin-service-diagnostic/DiagnosticAgent.java" "$PSScriptRoot/mixin-service-diagnostic/DiscoveryTrace.java"
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic compilation failed.' }
"Manifest-Version: 1.0`nPremain-Class: dev.moonrise.diagnostic.DiagnosticAgent`n`n" | Set-Content "$build/MANIFEST.MF" -Encoding ascii
& "$javaHome/bin/jar.exe" cfm "$build/diagnostic.jar" "$build/MANIFEST.MF" -C "$build/classes" dev
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic packaging failed.' }
$evidence = Join-Path $data ('logs/mixin-service-diagnostic-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence | Out-Null
@($loader, (Join-Path $env:USERPROFILE '.lunarclient/offline/multiver/genesis-0.1.0-SNAPSHOT-all.jar')) |
    ForEach-Object { Get-FileHash -LiteralPath $_ } | Select-Object Path,Hash |
    ConvertTo-Json | Set-Content "$evidence/original-hashes.before.json" -Encoding utf8
# Only the explicit development smoke reads these options into its structured MNR4 plan.
try {
    $env:MOONRISE_MIXIN_DIAGNOSTIC_AGENT = "$build/diagnostic.jar"
    $env:MOONRISE_MIXIN_DIAGNOSTIC_OPTIONS = "$loader|$evidence/discovery.log"
    Start-Process -FilePath $exe -ArgumentList '--dev-legacy-weave-smoke' -WindowStyle Hidden
} finally {
    Remove-Item Env:MOONRISE_MIXIN_DIAGNOSTIC_AGENT -ErrorAction SilentlyContinue
    Remove-Item Env:MOONRISE_MIXIN_DIAGNOSTIC_OPTIONS -ErrorAction SilentlyContinue
}
Write-Output "Diagnostic evidence: $evidence"
