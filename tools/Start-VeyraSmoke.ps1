param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
$workspace = Split-Path $appRoot -Parent
$source = Join-Path $workspace 'veyra/build/libs/Veyra-0.1.1-moonrise.1.jar'
$dataRoot = Join-Path $env:LOCALAPPDATA 'Moonrise/dev'
$binaryDirectory = Join-Path $appRoot 'runtime/current-synthetic-linkage-build/Release/net8.0-windows/win-x64'
$executable = Join-Path $binaryDirectory 'Moonrise.exe'
$loader = Join-Path $dataRoot 'cache/runtime/weave/weave-loader.jar'
$bridge = Join-Path $appRoot 'runtime/mapping-startup-build/Moonrise.Native.dll'
$linkageAgent = Join-Path $appRoot 'runtime/current-linkage-diagnostic/linkage-diagnostic.jar'
$expected = '779760392BCA1F269D0EB325E78950249D355E94A09C90988108C3CCD94D95BD'
if ((Get-FileHash -LiteralPath $source).Hash -ne $expected) { throw 'Full Veyra build changed; repeat offline inspection before smoke.' }
if ((Get-FileHash -LiteralPath $loader).Hash -ne '013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7') { throw 'Current loader integrity failure.' }
foreach ($path in @($executable, $bridge, $linkageAgent)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing smoke artifact: $path" }
}
$indexPath = Join-Path $dataRoot 'packages/metadata/index.json'
$settingsPath = Join-Path $dataRoot 'settings/moonrise-settings.json'
$indexBefore = [IO.File]::ReadAllBytes($indexPath)
$settingsBefore = [IO.File]::ReadAllBytes($settingsPath)
$index = [Text.Encoding]::UTF8.GetString($indexBefore) | ConvertFrom-Json
$settings = [Text.Encoding]::UTF8.GetString($settingsBefore) | ConvertFrom-Json
$selected = @($index.Packages | Where-Object { $_.Kind -eq 0 -and $_.Sha256 -eq $expected })
if ($selected.Count -ne 1) { throw 'Expected exactly one imported original matching the full Veyra build.' }
$modPath = Join-Path $dataRoot $selected[0].ManagedFilePath
if ((Get-FileHash -LiteralPath $modPath).Hash -ne $expected) { throw 'Imported Veyra integrity failure.' }
[void][Reflection.Assembly]::LoadFrom((Join-Path $binaryDirectory 'Moonrise.dll'))
$contract = [Moonrise.Compatibility.PackageInspector]::Inspect($modPath)
if ($contract.Requirements.WeaveGeneration.ToString() -ne 'Current' -or @($contract.Issues | Where-Object { $_.Severity.ToString() -eq 'Error' }).Count) { throw 'Current Weave contract check failed.' }
Write-Host 'Offline contract OK: full Veyra, current Weave 1.3.4, Java 17, declared initializer and Mixin resources present.'
if ($CheckOnly) { return }
if (Get-Process Moonrise -ErrorAction SilentlyContinue) { throw 'Close Moonrise before preparing smoke so it cannot overwrite package settings.' }
foreach ($name in @('JAVA_TOOL_OPTIONS','_JAVA_OPTIONS','JDK_JAVA_OPTIONS','MOONRISE_MIXIN_DIAGNOSTIC_AGENT','MOONRISE_MIXIN_DIAGNOSTIC_OPTIONS')) {
    if ([Environment]::GetEnvironmentVariable($name)) { throw "Clear $name to preserve smoke isolation." }
}
$evidence = Join-Path $dataRoot ('logs/veyra-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
[IO.File]::WriteAllBytes((Join-Path $evidence 'package-index.before.json'), $indexBefore)
[IO.File]::WriteAllBytes((Join-Path $evidence 'moonrise-settings.before.json'), $settingsBefore)
Copy-Item -LiteralPath $bridge -Destination (Join-Path $evidence 'Moonrise.Native.dll')
$previous = [Environment]::GetEnvironmentVariable('MOONRISE_DEV_STARTUP_CAPTURE')
$previousLinkage = [Environment]::GetEnvironmentVariable('MOONRISE_CURRENT_LINKAGE_AGENT')
$utf8 = New-Object Text.UTF8Encoding($false)
try {
    foreach ($package in $index.Packages) { $package.Enabled = $package.PackageId -eq $selected[0].PackageId }
    $settings.Client = 'lunar'
    $settings.MinecraftVersion = '1.8.9'
    $settings.CloseAfterLaunch = $false
    $settings.DisabledMods = @($index.Packages | Where-Object { $_.Kind -eq 0 -and -not $_.Enabled } | ForEach-Object OriginalFileName)
    $settings.DisabledAgents = @($index.Packages | Where-Object Kind -eq 1 | ForEach-Object OriginalFileName)
    [IO.File]::WriteAllText($indexPath, ($index | ConvertTo-Json -Depth 50), $utf8)
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 50), $utf8)
    @{ Backend='OfficialLunar'; Loader='1.3.4'; Adapter=[Moonrise.Services.CurrentWeaveSyntheticAdapter]::Id; VeyraSha256=$expected; EnabledMods=1; EnabledPackageAgents=0; Runtime='pending-manual-launch' } |
        ConvertTo-Json | Set-Content (Join-Path $evidence 'preparation.json') -Encoding utf8
    $env:MOONRISE_DEV_STARTUP_CAPTURE = $evidence
    $env:MOONRISE_CURRENT_LINKAGE_AGENT = $linkageAgent
    $moonrise = Start-Process -FilePath $executable -ArgumentList '--dev-weave-mod-smoke','--dev-jvm-startup-diagnostic' -WorkingDirectory $evidence -WindowStyle Hidden -PassThru
    Write-Host "Evidence: $evidence"
    Write-Host 'Click Launch exactly once in Moonrise. Check menu, Array List, Timer and SpeedMine in a local world, and Lunar account functionality. Close Minecraft normally, then close Moonrise.'
    $game = $null
    $exitCode = $null
    $jvmState = 'not-observed'
    while ($true) {
        if (-not $game) {
            $capture = Get-ChildItem -LiteralPath $evidence -Filter 'jvm-*-stdout.log' | Select-Object -First 1
            if ($capture) {
                $gameId = [int]([regex]::Match($capture.Name, 'jvm-(\d+)-').Groups[1].Value)
                try { $game = [Diagnostics.Process]::GetProcessById($gameId); $null = $game.Handle }
                catch { $jvmState = 'exited-before-monitor-attached'; break }
            }
        }
        if ($game -and $game.HasExited) { $exitCode = $game.ExitCode; $jvmState = 'exited'; break }
        if (-not $game -and $moonrise.HasExited) { break }
        Start-Sleep -Milliseconds 200
    }
    $null = Read-Host 'After closing Minecraft AND Moonrise, press Enter to collect evidence and restore package settings'
    if (-not $moonrise.HasExited) { throw "Moonrise still open. Close it, then restore the two *.before.json backups from $evidence manually." }
    $logs = @(Get-ChildItem -LiteralPath $evidence -Filter 'jvm-*.log' | ForEach-Object { Get-Content -LiteralPath $_.FullName }) -join "`n"
    $result = [ordered]@{
        Menu = (Read-Host 'Menu reached? [y/N]') -match '^(y|yes)$'
        Initializer = 'pending-log-review: require successful Main#preInit and Main#init, then Veyra client initialization evidence'
        ArrayList = (Read-Host 'Array List renders enabled modules in game? [y/N]') -match '^(y|yes)$'
        Timer = (Read-Host 'Timer changes game tick speed and restores it when disabled? [y/N]') -match '^(y|yes)$'
        SpeedMine = (Read-Host 'SpeedMine changes block breaking in game? [y/N]') -match '^(y|yes)$'
        LunarAccounts = (Read-Host 'Lunar account functionality works? [y/N]') -match '^(y|yes)$'
        JvmShutdown = (Read-Host 'JVM exited normally after closing Minecraft, without force kill? [y/N]') -match '^(y|yes)$'
        JvmState = $jvmState
        JvmExitCode = $exitCode
        LateMixinWarning = $logs -match 'Some mixins may be skipped'
        CurrentSyntheticDefinitions = @([regex]::Matches($logs, '(?im)^.*\[Moonrise current\] synthetic-defined name=.*') | ForEach-Object Value | Select-Object -Unique)
        MixinProcessing = @([regex]::Matches($logs, '(?im)^.*Mixing .* from veyra\.mixins\.json into .*') | ForEach-Object Value | Select-Object -Unique)
        MixinApplication = 'pending exported-bytecode review; processing lines and menu alone do not prove application'
        VeyraSha256After = (Get-FileHash -LiteralPath $modPath).Hash
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'result.json') -Encoding utf8
    Write-Host 'Review sanitized JVM logs and exported target bytecode for applied required Mixins; investigate any late warning before declaring success.'
} finally {
    [Environment]::SetEnvironmentVariable('MOONRISE_DEV_STARTUP_CAPTURE', $previous)
    [Environment]::SetEnvironmentVariable('MOONRISE_CURRENT_LINKAGE_AGENT', $previousLinkage)
    if (-not (Get-Process Moonrise -ErrorAction SilentlyContinue)) {
        [IO.File]::WriteAllBytes($indexPath, $indexBefore)
        [IO.File]::WriteAllBytes($settingsPath, $settingsBefore)
    }
}
