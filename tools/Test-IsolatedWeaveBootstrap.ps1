param(
    [switch]$TraceMixinBytesOnly,
    [string]$JavaHome = 'C:/Program Files/Java/jdk-25.0.3',
    [string]$RuntimeHome = "$env:USERPROFILE/.lunarclient/jre/5a6eb763f735cdeb6ad9d2c080e07ef2b949a771/zulu17.64.17-ca-jre17.0.18-win_x64"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('runtime/isolated-weave-bootstrap/' + [guid]::NewGuid().ToString('N'))
$inputs = @{
    loader = Join-Path $env:LOCALAPPDATA 'Moonrise/dev/cache/runtime/weave/weave-loader.jar'
    raven = Join-Path $env:LOCALAPPDATA 'Moonrise/packages/weave/RavenWeave-1.1.4.jar'
    bwh = Join-Path $env:LOCALAPPDATA 'Moonrise/adapters/maintained-builds/6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57.jar'
    api = Join-Path $env:USERPROFILE '.weave/.maven-repository/net/weavemc/api/api-v1_8/1.3.4/api-v1_8-1.3.4.jar'
    vanilla = Join-Path $env:APPDATA '.minecraft/versions/1.8.9/1.8.9.jar'
    mappings = Join-Path $env:USERPROFILE '.weave/.cache/mappings/merged_1.8.9/mappings.tiny'
    game = Join-Path $env:USERPROFILE '.weave/.cache/jars/mcp-named_1.8.9/d6e41a11.cache'
    guava = Join-Path $env:APPDATA '.minecraft/libraries/com/google/guava/guava/17.0/guava-17.0.jar'
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
    raven = 'C6EDD2979121DF4F7C2585ABEE8DA0B84E3B7B5496B0B1A4A684EDAD179727A1'
    bwh = '6DA5B2A67E05E07DEAE2779B4AA3D1B70E5028DED227C41487FF783F5D609A57'
    api = '6C1BC99BE8B94DE44AF64D2E1AB08CACD2E1C5D8F2423C113041F02D3228D11A'
    vanilla = '14F0D96D1A56FB4F5C3B2233D00699525893FE5CE3DCF181E7DE59120595D298'
    mappings = '20168DBF681542448822881C4C28C21200F7FBA335BADA1C021CD887F8A74545'
    game = 'EBFAA3DEA20F7BAB9793503558544F6965B080B62CBF5FCFFDE797F91765F5CF'
    guava = '8C36A80EA613D0B6B8040A17CF837C5BBE3677BC1B06A058A6C174FDB787EBBC'
}
foreach ($key in $inputs.Keys) { if ($before[$key] -ne $pinned[$key]) { throw "Pinned input mismatch: $key" } }
foreach ($dir in @('inputs', 'classes', 'runner', 'home', 'work', 'temp', 'logs', 'appdata', 'localappdata')) {
    New-Item -ItemType Directory -Path (Join-Path $run $dir) -Force | Out-Null
}
git -C $root check-ignore $run | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Artifacts must be ignored' }
try {
    foreach ($key in $inputs.Keys) {
        Copy-Item -LiteralPath $inputs[$key] -Destination $copies[$key]
        if ((Get-FileHash -LiteralPath $copies[$key]).Hash -ne $before[$key]) { throw 'Copy hash mismatch' }
    }
    $loader = "$run/inputs/loader.jar"
    $audit = & "$JavaHome/bin/javap.exe" -classpath $loader -c -p net.weavemc.loader.impl.WeaveLoader net.weavemc.internals.UtilKt net.weavemc.loader.impl.util.UtilityKt net.weavemc.loader.impl.bootstrap.cache.CacheManager net.weavemc.loader.impl.util.FileManager net.weavemc.loader.impl.util.MappingsHandler net.weavemc.internals.MappingsRetrieval net.weavemc.loader.impl.util.SystemPropertyDelegate
    if ($LASTEXITCODE) { throw 'Filesystem audit failed' }
    $audit | Set-Content "$run/logs/filesystem-bytecode.txt"
    & "$JavaHome/bin/javap.exe" -classpath $copies['raven'] -c -p me.pianopenguin471.RavenWeave me.pianopenguin471.command.GUICommand |
        Set-Content "$run/logs/raven-command-bytecode.txt"
    if ($LASTEXITCODE) { throw 'Original Raven command bytecode audit failed' }
    $joined = $audit -join "`n"
    foreach ($required in @('String user.home', 'String .weave', 'String weave.repo.offline.enabled', 'String weave.repo.local.path', 'String weave.vanilla.jar.path', 'CacheManager.cleanup', 'Instrumentation.addTransformer', 'Method finalize:')) {
        if (!$joined.Contains($required)) { throw "Pinned bootstrap audit changed: $required" }
    }
    if ($joined -match 'String (?:[A-Za-z]:[\\/])') { throw 'Unbounded filesystem root; stop before launch' }
    # Compile existing parser/interception helpers unchanged; no existing agent is activated.
    $legacy = Join-Path $env:LOCALAPPDATA 'Moonrise/dev/cache/runtime/weave/weave-loader-0.2.6.jar'
    & "$JavaHome/bin/javap.exe" -classpath $legacy -c -p net.weavemc.loader.api.command.CommandBus net.weavemc.loader.api.command.Command |
        Set-Content "$run/logs/legacy-commandbus-bytecode.txt"
    if ($LASTEXITCODE) { throw 'Legacy CommandBus bytecode audit failed' }
    & "$JavaHome/bin/javap.exe" -classpath $legacy -p -s -c -v 'net.weavemc.loader.api.event.StartGameEvent$Post' net.weavemc.loader.api.event.StartGameEvent net.weavemc.loader.api.event.Event net.weavemc.loader.api.event.KeyboardEvent net.weavemc.loader.api.event.ShutdownEvent net.weavemc.loader.api.event.SubscribeEvent |
        Set-Content "$run/logs/legacy-event-abi-bytecode.txt"
    if ($LASTEXITCODE) { throw 'Legacy event ABI bytecode audit failed' }
    $sources = @(Get-ChildItem "$PSScriptRoot/mixed-weave-poc" -Recurse -Filter *.java | ForEach-Object FullName)
    $sources += @(Get-ChildItem "$PSScriptRoot/isolated-weave-bootstrap" -Filter *.java | ForEach-Object FullName)
    & "$JavaHome/bin/javac.exe" --release 17 -cp "$loader;$legacy" -d "$run/classes" @sources
    if ($LASTEXITCODE) { throw 'Offline runner compilation failed' }
    # Export CommandBus and its immediate event ABI dependencies using the existing proof.
    # Old loader is a build-time source archive, never a bootstrap/runtime classpath entry.
    & "$JavaHome/bin/java.exe" -cp "$run/classes;$loader" OfflineLegacyCommand $legacy $copies['raven'] "$run/classes" $copies['api'] "$run/runner"
    if ($LASTEXITCODE) { throw 'Legacy Command contract export failed' }
    # Preserve existing compatibility helpers, excluding the separate proof's
    # Minecraft/LWJGL fixtures from the real-input provider classpath.
    Get-ChildItem "$run/classes" -Recurse -File -Filter *.class | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath("$run/classes", $_.FullName)
        if ($relative -match '^(net[\\/]minecraft|org[\\/]lwjgl|fixture)[\\/]') { return }
        $destination = Join-Path "$run/runner" $relative
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination
    }
    "Manifest-Version: 1.0`nPremain-Class: OfflineBootstrapAgent`n`n" | Set-Content "$run/agent.mf" -Encoding ascii
    & "$JavaHome/bin/jar.exe" cfm "$run/test-only-agent.jar" "$run/agent.mf" -C "$run/classes" OfflineBootstrapAgent.class -C "$run/classes" 'OfflineBootstrapAgent$1.class'
    if ($LASTEXITCODE) { throw 'Test-only agent packaging failed' }
    $repo = "$run/home/.weave/.maven-repository"
    $artifact = "$repo/net/weavemc/api/api-v1_8/1.3.4"
    New-Item -ItemType Directory -Path $artifact -Force | Out-Null
    Copy-Item "$run/inputs/api.jar" "$artifact/api-v1_8-1.3.4.jar"
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
    $arguments = @('-Xverify:all', '-Djava.awt.headless=true', '-Dmoonrise.proof.isolatedBootstrap=true', '-Djava.security.manager=allow', "-Duser.home=$run/home", "-Duser.dir=$run/work", "-Djava.io.tmpdir=$run/temp", "-Dweave.repo.local.path=$repo", "-Dweave.vanilla.jar.path=$run/inputs/vanilla.jar", '-Dweave.repo.offline.enabled=true', '-Dweave.environment.version=1.8.9', '-Dweave.namespace.environment.runtime=mcp-named', '-Dweave.namespace.environment.classpath=mcp-named', "-javaagent:$run/test-only-agent.jar", '-cp', "$run/classes;$loader", 'OfflineBootstrap', $run, "$run/inputs/raven.jar", "$run/inputs/bwh.jar")
    if ($TraceMixinBytesOnly) { $arguments = @('-Dmoonrise.proof.traceMixinBytesOnly=true') + $arguments }
    # System owns both appended mod definitions and the sole named game archive.
    # Official vanilla remains FileManager input only, never a classpath entry.
    $arguments[$arguments.IndexOf('-cp') + 1] = "$run/runner;$loader;$run/inputs/game.jar;$run/inputs/guava.jar"
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
    $stdout.Result
    $stderr.Result
    "Offline JVM exit=$($process.ExitCode); evidence=$run"
    if ($timedOut) { throw 'Bootstrap timed out; captured logs identify last reached stage' }
    if ($process.ExitCode -ne 2 -and !($process.ExitCode -eq 1 -and
        $stderr.Result.Contains('Somehow minecraftBootstrapClassLoader is null'))) {
        throw 'Unexpected runner exit; inspect first bootstrap blocker'
    }
    if (!$TraceMixinBytesOnly) {
        foreach ($required in @('PASS SANDBOX named game net/minecraft/entity/Entity;',
            'PASS SANDBOX named game net/minecraft/client/gui/GuiScreen;',
            'GAME definition requested net/minecraft/client/gui/GuiScreen; owner=jdk.internal.loader.ClassLoaders$AppClassLoader',
            'PASS legacy Command single/repeated identity', 'PASS current Command independent',
            'PASS Guava unique definitions; Maps single/repeated identity; owner=jdk.internal.loader.ClassLoaders$AppClassLoader',
            'PASS original Raven entrypoint bytes unchanged; initializeMods owner=')) {
            if (!$stdout.Result.Contains($required)) { throw "Missing provider evidence: $required" }
        }
        if ($stdout.Result -match 'NoClassDefFoundError: net/weavemc/loader/api/command/Command(?:\r?\n|$)') {
            throw 'Legacy Command remains unresolved by actual loader'
        }
        if (($stdout.Result + $stderr.Result).Contains('NoClassDefFoundError: com/google/common/collect/Maps')) {
            throw 'Guava Maps remains unresolved'
        }
        foreach ($required in @('PASS legacy CommandBus single/repeated identity',
            'PASS Raven CommandBus identity equals backing Owner',
            'PASS actual original Raven preInit returned; init deferred',
            'PASS actual Raven getDeclaredMethods; three handler identities; EventBus.subscribe(this) completed once',
            'PASS actual bootstrap original GUICommand registered exactly once; name=clickgui; aliases=[gui, raven]')) {
            if (!$stdout.Result.Contains($required)) { throw "Missing CommandBus evidence: $required" }
        }
        if ([regex]::Matches($stdout.Result, 'PASS actual original Raven preInit returned; init deferred').Count -ne 1 -or
            ($stdout.Result + $stderr.Result).Contains('RavenWeave#preInit:')) {
            throw 'Original Raven preInit did not complete exactly once'
        }
        foreach ($type in @('StartGameEvent', 'StartGameEvent$Post', 'KeyboardEvent', 'ShutdownEvent', 'SubscribeEvent')) {
            if (!$stdout.Result.Contains("PASS shared legacy ABI identity AppClassLoader/backing Owner net.weavemc.loader.api.event.$type")) {
                throw "Missing event class identity: $type"
            }
        }
        if (($stdout.Result + $stderr.Result) -match 'SANDBOX missing target|@Mixin target .* was not found|AssertionError') {
            throw 'Provider/target validation failed'
        }
        if ([regex]::Matches($stdout.Result, 'PASS current provider/archive equality xyz/mangal/bwhelper/mixins/').Count -ne 10) {
            throw 'Incomplete current Mixin byte equality evidence'
        }
        if ([regex]::Matches($stdout.Result, 'METADATA_READ [^\r\n]+legacyAdaptation=true').Count -ne 1 -or
            [regex]::Matches($stdout.Result, 'METADATA_READ [^\r\n]+legacyAdaptation=false').Count -ne 3) {
            throw 'Metadata parser stream evidence changed'
        }
    }
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
    'PASS original and isolated loader/Raven/BWH/API/vanilla/mappings/named-game/Guava hashes unchanged'
}
