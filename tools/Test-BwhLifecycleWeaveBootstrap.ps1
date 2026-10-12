# BWH-only lifecycle snapshot of the verified offline runner. Shared bootstrap/guards
# remain in DefinitionOnlyBootstrap.java; do not weaken either runner's guards.
# No execution without the explicit switch; preparation does not certify PASS.
param(
    [switch]$ExecuteGuardedProbe,
    [string]$JavaHome = 'C:/Program Files/Java/jdk-25.0.3',
    [string]$RuntimeHome = "$env:USERPROFILE/.lunarclient/jre/5a6eb763f735cdeb6ad9d2c080e07ef2b949a771/zulu17.64.17-ca-jre17.0.18-win_x64"
)
$ErrorActionPreference = 'Stop'
if (!$ExecuteGuardedProbe) { throw "Prepared only; use -ExecuteGuardedProbe for separately authorized execution." }
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('runtime/bwh-lifecycle-bootstrap/' + [guid]::NewGuid().ToString('N'))
$inputs = @{
    loader = Join-Path $env:LOCALAPPDATA 'Moonrise/dev/cache/runtime/weave/weave-loader.jar'
    bwh = Join-Path $env:LOCALAPPDATA 'Moonrise/adapters/maintained-builds/6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57.jar'
    api = Join-Path $env:USERPROFILE '.weave/.maven-repository/net/weavemc/api/api-v1_8/1.3.4/api-v1_8-1.3.4.jar'
    vanilla = Join-Path $env:APPDATA '.minecraft/versions/1.8.9/1.8.9.jar'
    mappings = Join-Path $env:USERPROFILE '.weave/.cache/mappings/merged_1.8.9/mappings.tiny'
    game = Join-Path $env:USERPROFILE '.weave/.cache/jars/mcp-named_1.8.9/d6e41a11.cache'
    guava = Join-Path $env:APPDATA '.minecraft/libraries/com/google/guava/guava/17.0/guava-17.0.jar'
    log4jApi = Join-Path $env:APPDATA '.minecraft/libraries/org/apache/logging/log4j/log4j-api/2.22.1/log4j-api-2.22.1.jar'
    log4jCore = Join-Path $env:APPDATA '.minecraft/libraries/org/apache/logging/log4j/log4j-core/2.22.1/log4j-core-2.22.1.jar'
}
$before = @{}
$copies = @{}
foreach ($key in $inputs.Keys) {
    $extension = if ($key -eq 'mappings') { 'tiny' } else { 'jar' }
    $copies[$key] = "$run/inputs/$key.$extension"
}
foreach ($key in $inputs.Keys) { $before[$key] = (Get-FileHash -LiteralPath $inputs[$key] -Algorithm SHA256).Hash }
$pinned = @{
    loader = '013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7'
    bwh = '6DA5B2A67E05E07DEAE2779B4AA3D1B70E5028DED227C41487FF783F5D609A57'
    api = '6C1BC99BE8B94DE44AF64D2E1AB08CACD2E1C5D8F2423C113041F02D3228D11A'
    vanilla = '14F0D96D1A56FB4F5C3B2233D00699525893FE5CE3DCF181E7DE59120595D298'
    mappings = '20168DBF681542448822881C4C28C21200F7FBA335BADA1C021CD887F8A74545'
    game = 'EBFAA3DEA20F7BAB9793503558544F6965B080B62CBF5FCFFDE797F91765F5CF'
    guava = '8C36A80EA613D0B6B8040A17CF837C5BBE3677BC1B06A058A6C174FDB787EBBC'
    log4jApi = '5D7BEAE7FF15D8516D6517121D7F12A79A6AC180DF64B5FCEC55D5BE21056E53'
    log4jCore = '46DCCECAC556623D8E2CE8648496824A82951D139062A4E61148AFF1A25ED18D'
}
foreach ($key in $inputs.Keys) { if ($before[$key] -ne $pinned[$key]) { throw "Pinned input mismatch: $key" } }
foreach ($dir in @('inputs', 'classes', 'runner', 'home', 'work', 'temp', 'logs', 'appdata', 'localappdata')) {
    New-Item -ItemType Directory -Path (Join-Path $run $dir) -Force | Out-Null
}
git -C $root check-ignore $run | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Artifacts must be ignored' }
$validationPassed = $false
try {
    foreach ($key in $inputs.Keys) {
        Copy-Item -LiteralPath $inputs[$key] -Destination $copies[$key]
        if ((Get-FileHash -LiteralPath $copies[$key]).Hash -ne $before[$key]) { throw "Copy hash mismatch: $key" }
    }
    $loader = $copies['loader']
    $types = @('net.weavemc.loader.impl.bootstrap.Bootstrap', 'net.weavemc.loader.impl.bootstrap.Bootstrap$bootstrap$1', 'net.weavemc.loader.impl.bootstrap.transformer.ApplicationWrapper', 'net.weavemc.loader.impl.bootstrap.transformer.ApplicationWrapper$WrappingLoader', 'net.weavemc.loader.impl.bootstrap.transformer.URLClassLoaderTransformer', 'net.weavemc.loader.impl.WeaveLoader', 'net.weavemc.loader.impl.InjectionHandler', 'net.weavemc.loader.impl.util.UtilityKt', 'net.weavemc.loader.impl.util.AnalyticsKt', 'net.weavemc.loader.impl.util.MappingsHandler', 'net.weavemc.loader.impl.util.FileManager')
    & "$JavaHome/bin/javap.exe" -classpath $loader -c -p @types | Set-Content "$run/logs/bootstrap-bytecode.txt"
    if ($LASTEXITCODE) { throw 'Audit failed' }
    & "$JavaHome/bin/javap.exe" -classpath $copies['bwh'] -c -p lunarweavebridge.compat.BwhPreInitBridge lunarweavebridge.compat.PreInitSupport xyz.mangal.bwhelper.BWHelper xyz.mangal.bwhelper.mods.SlumberBot xyz.mangal.bwhelper.mods.SimHelper |
        Set-Content "$run/logs/bwh-lifecycle-bytecode.txt"
    if ($LASTEXITCODE) { throw 'BWH lifecycle preflight audit failed' }
    # Exact runtime JDK classes; Color attempts AWT native loading even headless.
    & "$JavaHome/bin/javap.exe" --system $RuntimeHome -c -p java.awt.Color java.awt.Toolkit 'java.awt.Toolkit$2' |
        Set-Content "$run/logs/awt-native-bytecode.txt"
    if ($LASTEXITCODE) { throw 'Runtime AWT preflight audit failed' }
    & "$JavaHome/bin/javac.exe" --release 17 -cp $loader -d "$run/classes" "$PSScriptRoot/isolated-weave-bootstrap/DefinitionOnlyBootstrap.java" "$PSScriptRoot/isolated-weave-bootstrap/ApiDefinitionOnlyBootstrap.java" "$PSScriptRoot/isolated-weave-bootstrap/BwhLifecycleBootstrap.java"
    if ($LASTEXITCODE) { throw 'Compilation failed' }
    "Manifest-Version: 1.0`nPremain-Class: DefinitionOnlyBootstrap`n`n" | Set-Content "$run/agent.mf" -Encoding ascii
    & "$JavaHome/bin/jar.exe" cfm "$run/test-only-agent.jar" "$run/agent.mf" -C "$run/classes" .
    if ($LASTEXITCODE) { throw 'Packaging failed' }
    $repo = "$run/home/.weave/.maven-repository"
    $artifact = "$repo/net/weavemc/api/api-v1_8/1.3.4"
    New-Item -ItemType Directory -Path $artifact -Force | Out-Null
    Copy-Item "$run/inputs/api.jar" "$artifact/api-v1_8-1.3.4.jar"
    if ((Get-FileHash "$artifact/api-v1_8-1.3.4.jar").Hash -ne $before['api']) { throw "Offline API hash mismatch" }
    # The pinned property delegate evaluates its default provider even when the
    # override is present. Satisfy unchanged Windows lookup inside isolated home.
    $vanillaDefaultDirectory = "$run/home/AppData/Roaming/.minecraft/versions/1.8.9"
    New-Item -ItemType Directory -Path $vanillaDefaultDirectory -Force | Out-Null
    Copy-Item "$run/inputs/vanilla.jar" "$vanillaDefaultDirectory/1.8.9.jar"
    if ((Get-FileHash "$vanillaDefaultDirectory/1.8.9.jar").Hash -ne $before['vanilla']) { throw 'Default vanilla copy hash mismatch' }
    # Existing MERGED Tiny v2 cache is a supported input, not a raw artifact substitute.
    $mappingCache = "$run/home/.weave/.cache/mappings/merged_1.8.9/mappings.tiny"
    if ((Get-Content -LiteralPath $inputs['mappings'] -TotalCount 1) -ne "tiny`t2`t0`tofficial`tmcp-srg`tmcp-named") { throw 'Unexpected merged mapping format/namespaces' }
    New-Item -ItemType Directory -Path (Split-Path $mappingCache) -Force | Out-Null
    Copy-Item -LiteralPath $copies['mappings'] -Destination $mappingCache
    if ((Get-FileHash $mappingCache).Hash -ne $before['mappings']) { throw 'Mapping cache copy hash mismatch' }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "$RuntimeHome/bin/java.exe"
    $psi.WorkingDirectory = "$run/work"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($key in @('JAVA_TOOL_OPTIONS', '_JAVA_OPTIONS', 'JDK_JAVA_OPTIONS', 'CLASSPATH')) { $psi.EnvironmentVariables.Remove($key) }
    foreach ($pair in @(@('USERPROFILE','home'), @('HOME','home'), @('APPDATA','appdata'), @('LOCALAPPDATA','localappdata'), @('TEMP','temp'), @('TMP','temp'))) {
        $psi.EnvironmentVariables[$pair[0]] = "$run/$($pair[1])"
    }
    $arguments = @('-Xverify:all', '-Xlog:class+init=info', '-Djava.awt.headless=true', '-Djava.security.manager=allow', '-Dweave.api.minecraft.enabled=true', '-Dklog.logger.net.weavemc.loader.impl.WeaveLoader.level=INFO', "-Duser.home=$run/home", "-Duser.dir=$run/work", "-Djava.io.tmpdir=$run/temp", "-Dweave.repo.local.path=$repo", "-Dweave.vanilla.jar.path=$run/inputs/vanilla.jar", '-Dweave.repo.offline.enabled=true', '-Dweave.environment.version=1.8.9', '-Dweave.namespace.environment.runtime=mcp-named', '-Dweave.namespace.environment.classpath=mcp-named', "-javaagent:$run/test-only-agent.jar", '-cp', "$run/classes;$loader;$run/inputs/game.jar;$run/inputs/guava.jar;$run/inputs/log4jApi.jar;$run/inputs/log4jCore.jar", 'BwhLifecycleBootstrap', $run)
    $psi.Arguments = ($arguments | ForEach-Object { if ($_ -match '["\r\n]') { throw 'Invalid JVM argument' }; '"' + $_ + '"' }) -join ' '
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = !$process.WaitForExit(45000)
    if ($timedOut) { $process.Kill(); $process.WaitForExit() }
    $stdout.Result | Set-Content "$run/logs/bootstrap.stdout.txt"
    $stderr.Result | Set-Content "$run/logs/bootstrap.stderr.txt"
    ($stdout.Result -split '\r?\n') | Select-String '^PASS|^REGISTER|^ORIGINAL|^MAIN callback'
    $stderr.Result
    "Offline JVM exit=$($process.ExitCode); evidence=$run"
    [pscustomobject]@{ ExitCode = $process.ExitCode; TimedOut = $timedOut;
        FullPreInitMarker = $stdout.Result.Contains('BWH_LIFECYCLE_READY');
        NativeDenied = ($stdout.Result + $stderr.Result).Contains('Native load denied');
        Status = 'BLOCKED pending complete lifecycle and outer validation' } |
        ConvertTo-Json | Set-Content "$run/logs/lifecycle-result.json"
    if ($timedOut -or $process.ExitCode -ne 0) { throw 'BLOCKED BWH lifecycle probe; inspect logs' }
    if (!$stdout.Result.Contains('PASS offline lifecycle bootstrap;')) { throw 'BLOCKED missing genuine BWH bootstrap proof' }
    if ([regex]::Matches($stdout.Result, '(?m)^BWH_LIFECYCLE_READY original bridge started=true; full preInit returned; genuine bootstrap; BWH-only\r?$').Count -ne 1) { throw 'BLOCKED original BWH preInit incomplete' }
    $combined = $stdout.Result + $stderr.Result
    if ([regex]::Matches($combined, 'Successfully registered Minecraft API \(net\.weavemc\.api:api-v1_8:1\.3\.4\)').Count -ne 1) { throw 'BLOCKED API completion marker count' }
    # SLF4J's optional binding diagnostic is not a Weave/Klog failure. Preserve
    # it in full stderr; accept only the exact known three-line NOP fallback.
    $slf4jFallback = @('SLF4J: Failed to load class "org.slf4j.impl.StaticLoggerBinder".',
        'SLF4J: Defaulting to no-operation (NOP) logger implementation',
        'SLF4J: See http://www.slf4j.org/codes.html#StaticLoggerBinder for further details.')
    $fallbackPattern = ($slf4jFallback | ForEach-Object { [regex]::Escape($_) }) -join '\r?\n'
    $reviewed = [regex]::Replace($combined, $fallbackPattern, '')
    # Case-sensitive severity names avoid matching JVM verification of java.lang.Error.
    if ($reviewed -cmatch 'Failed to|Could not load access widener|\bERROR\b|\bFATAL\b|Exception thrown|Exception in thread |HeadlessException|Native load denied|Offline network denied|Child process denied|Outside-root write denied') { throw 'BLOCKED loader/lifecycle failure or guard denial; inspect full logs' }
    $validationPassed = $true
} finally {
    foreach ($key in $inputs.Keys) {
        if ((Get-FileHash -LiteralPath $inputs[$key]).Hash -ne $before[$key]) { throw "Original changed: $key" }
        if ((Get-FileHash -LiteralPath $copies[$key]).Hash -ne $before[$key]) { throw "Isolated input changed: $key" }
    }
    $before | ConvertTo-Json | Set-Content "$run/logs/input-hashes.json"
    Get-ChildItem -LiteralPath $run -Recurse -File | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($run, $_.FullName); Length = $_.Length }
    } | ConvertTo-Json | Set-Content "$run/logs/filesystem-inventory.json"
    if ($vanillaDefaultDirectory -and (Get-FileHash "$vanillaDefaultDirectory/1.8.9.jar").Hash -ne $before['vanilla']) { throw 'Default vanilla copy changed' }
    if ($mappingCache -and (Get-FileHash $mappingCache).Hash -ne $before['mappings']) { throw 'Isolated mapping cache changed' }
    if ($artifact -and (Get-FileHash "$artifact/api-v1_8-1.3.4.jar").Hash -ne $before['api']) { throw "Offline API changed" }
    if ($validationPassed) {
        [pscustomobject]@{ ExitCode = $process.ExitCode; TimedOut = $timedOut;
            FullPreInitMarker = $true; NativeDenied = $false;
            Status = 'PASS BWH-only complete preInit; logs and immutable inputs validated' } |
            ConvertTo-Json | Set-Content "$run/logs/lifecycle-result.json"
    }
    'PASS all nine original and isolated inputs unchanged (including Log4j API/Core)'
}

'PASS guarded BWH-only full preInit lifecycle; no Main.main/game loop'
