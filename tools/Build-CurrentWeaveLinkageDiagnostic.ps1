param([string]$JavaHome = 'C:/Program Files/Java/jdk-25.0.3')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'runtime/current-linkage-diagnostic'
$loader = Join-Path $env:LOCALAPPDATA 'Moonrise/dev/cache/runtime/weave/weave-loader.jar'
if ((Get-FileHash -LiteralPath $loader).Hash -ne '013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7') { throw 'Untouched current loader hash mismatch.' }
New-Item -ItemType Directory -Force -Path "$build/classes" | Out-Null
& "$JavaHome/bin/javac.exe" --release 17 -d "$build/classes" "$PSScriptRoot/current-weave-probe/LinkageTrace.java" "$PSScriptRoot/current-weave-probe/LinkageTraceTest.java"
if ($LASTEXITCODE) { throw 'Diagnostic compilation failed.' }
$testLog = Join-Path $build ('offline-' + [guid]::NewGuid().ToString('N') + '.log')
& "$JavaHome/bin/java.exe" -cp "$build/classes;$loader" dev.moonrise.diagnostic.LinkageTraceTest $testLog
if ($LASTEXITCODE) { throw 'Offline diagnostic verification failed.' }
"Manifest-Version: 1.0`nPremain-Class: dev.moonrise.diagnostic.LinkageTrace`n`n" | Set-Content "$build/MANIFEST.MF" -Encoding ascii
& "$JavaHome/bin/jar.exe" cfm "$build/linkage-diagnostic.jar" "$build/MANIFEST.MF" -C "$build/classes" dev/moonrise/diagnostic/LinkageTrace.class
if ($LASTEXITCODE) { throw 'Diagnostic packaging failed.' }
Write-Output "$build/linkage-diagnostic.jar"
