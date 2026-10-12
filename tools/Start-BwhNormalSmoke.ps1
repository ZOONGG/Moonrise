param([switch]$CheckOnly, [switch]$PrepareOnly, [switch]$RestoreOnly)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
$dataRoot = Join-Path $env:LOCALAPPDATA 'Moonrise/dev'
$binaryDirectory = Join-Path $appRoot 'runtime/bwh-normal-validation/bin/Moonrise/release_win-x64'
$executable = Join-Path $binaryDirectory 'Moonrise.exe'
$bridge = Join-Path $appRoot 'runtime/mapping-startup-build/Moonrise.Native.dll'
$indexPath = Join-Path $dataRoot 'packages/metadata/index.json'
$settingsPath = Join-Path $dataRoot 'settings/moonrise-settings.json'
$pendingPath = Join-Path $dataRoot 'logs/bwh-normal-smoke.pending.txt'
$expected = '6DA5B2A67E05E07DEAE2779B4AA3D1B70E5028DED227C41487FF783F5D609A57'
function Restore-Selection([string]$directory) {
    if (Get-Process Moonrise -ErrorAction SilentlyContinue) { throw 'Close Moonrise before restoring selections.' }
    [IO.File]::WriteAllBytes($indexPath, [IO.File]::ReadAllBytes((Join-Path $directory 'package-index.before.json')))
    [IO.File]::WriteAllBytes($settingsPath, [IO.File]::ReadAllBytes((Join-Path $directory 'moonrise-settings.before.json')))
    Remove-Item -LiteralPath $pendingPath
}
if ($RestoreOnly) {
    Restore-Selection ([IO.File]::ReadAllText($pendingPath).Trim())
    Write-Host 'Original Moonrise selections restored.'
    return
}
foreach ($path in @($executable, $bridge)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing artifact: $path" }
}
$loader = Join-Path $dataRoot 'cache/runtime/weave/weave-loader.jar'
if ((Get-FileHash -LiteralPath $loader).Hash -ne '013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7') { throw 'Current loader integrity failure.' }
if ((Get-FileHash -LiteralPath $bridge).Hash -ne '0A1583307A742A7456DF7657D21EB59A7F7157235D3532C53737BD85E43E9775') { throw 'Capture bridge changed; repeat verification.' }
$indexBefore = [IO.File]::ReadAllBytes($indexPath)
$settingsBefore = [IO.File]::ReadAllBytes($settingsPath)
$index = [Text.Encoding]::UTF8.GetString($indexBefore) | ConvertFrom-Json
$settings = [Text.Encoding]::UTF8.GetString($settingsBefore) | ConvertFrom-Json
$selected = @($index.Packages | Where-Object { $_.Kind -eq 0 -and $_.Sha256 -eq $expected })
if ($selected.Count -ne 1) { throw 'Expected one imported BWH migration build with the inspected hash.' }
$modPath = Join-Path $dataRoot $selected[0].ManagedFilePath
if ((Get-FileHash -LiteralPath $modPath).Hash -ne $expected) { throw 'BWH integrity failure.' }
[void][Reflection.Assembly]::LoadFrom((Join-Path $binaryDirectory 'Moonrise.dll'))
$contract = [Moonrise.Compatibility.PackageInspector]::Inspect($modPath)
if ($contract.Requirements.WeaveGeneration.ToString() -ne 'Current' -or $contract.Requirements.StartupAgent -or
    @($contract.Issues | Where-Object { $_.Severity.ToString() -eq 'Error' }).Count) { throw 'BWH contract validation failed.' }
$parser = [Moonrise.Services.JarMetadataParser]::new()
$mod = $parser.ParseWeaveMod($modPath)
if ([Moonrise.Services.WeaveModApiInspector]::new().Inspect($mod).Generation.ToString() -ne 'Current') { throw 'Loader generation mismatch.' }
Write-Host 'Contract OK: existing BWH migration JAR; current Weave; Minecraft 1.8.9; Java >=17; no declared dependencies; no Premain-Class.'
if ($CheckOnly) { return }
if (Get-Process Moonrise -ErrorAction SilentlyContinue) { throw 'Close Moonrise before preparing or starting the smoke.' }
foreach ($name in @('JAVA_TOOL_OPTIONS','_JAVA_OPTIONS','JDK_JAVA_OPTIONS','MOONRISE_MIXIN_DIAGNOSTIC_AGENT','MOONRISE_MIXIN_DIAGNOSTIC_OPTIONS','MOONRISE_CURRENT_LINKAGE_AGENT')) {
    if ([Environment]::GetEnvironmentVariable($name)) { throw "Clear $name before this smoke." }
}
$utf8 = [Text.UTF8Encoding]::new($false)
if (Test-Path -LiteralPath $pendingPath) {
    $evidence = [IO.File]::ReadAllText($pendingPath).Trim()
    $prepared = Get-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $indexPath).Hash -ne $prepared.PreparedIndexSha256 -or
        (Get-FileHash -LiteralPath $settingsPath).Hash -ne $prepared.PreparedSettingsSha256 -or
        (Get-FileHash -LiteralPath $executable).Hash -ne $prepared.ExecutableSha256) {
        throw 'Prepared configuration/build changed. Use -RestoreOnly, then prepare again.'
    }
} else {
    $evidence = Join-Path $dataRoot ('logs/bwh-normal-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $evidence | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $evidence 'package-index.before.json'), $indexBefore)
    [IO.File]::WriteAllBytes((Join-Path $evidence 'moonrise-settings.before.json'), $settingsBefore)
    # Save the recovery pointer before any settings mutation.
    [IO.File]::WriteAllText($pendingPath, $evidence, $utf8)
    try {
        Copy-Item -LiteralPath $bridge -Destination (Join-Path $evidence 'Moonrise.Native.dll')
        $contract | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $evidence 'bwh-inspection.json') -Encoding utf8
        $paths = [Moonrise.Infrastructure.AppPaths]::new($dataRoot, $appRoot, [Moonrise.Infrastructure.AppMode]::Development)
        $resolution = [Moonrise.Services.MaintainedCompatibilityBuildService]::new($paths, $parser).Resolve('1.8.9', [Moonrise.Models.PackageInfo[]]@($mod))
        if ($resolution.AppliedBuilds.Count) { throw 'Selected imported BWH unexpectedly needs another maintained build.' }
        $network = [Moonrise.Services.BwhNetworkAgentDeploymentService]::new($paths).Ensure()
        $plan = [Moonrise.Services.CurrentLaunchPlanFactory]::new().Create([Moonrise.Models.PackageInfo[]]@($mod), [Moonrise.Models.PackageInfo[]]@(), $loader, $false, $null, $network)
        # Exercise normal preparation and MNR4 serialization offline, without starting any process.
        $offline = Join-Path $dataRoot ('temp/bwh-offline-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $offline | Out-Null
        try {
            $mods = [Moonrise.Services.EnabledModDirectoryService]::new($parser).Create($offline, [Moonrise.Models.PackageInfo[]]@($mod))
            $plan = [Moonrise.Services.CurrentWeaveSyntheticAdapter]::Prepare($plan, $offline)
            $config = [Moonrise.Services.NativeBridgeConfigurationBuilder]::BuildLaunchPlan($plan, $mods)
            [IO.File]::WriteAllText((Join-Path $evidence 'offline-mnr4.txt'), $config, $utf8)
            $agents = @($plan.Agents | ForEach-Object { @{ Id=$_.RuntimeId; Role=$_.Role.ToString(); Sha256=(Get-FileHash -LiteralPath $_.Path).Hash } })
            if (@($plan.Agents | Where-Object { $_.Role.ToString() -eq 'PackageAgent' }).Count) { throw 'Unexpected user agent.' }
        } finally {
            $tempRoot = [IO.Path]::GetFullPath((Join-Path $dataRoot 'temp')) + [IO.Path]::DirectorySeparatorChar
            if (-not [IO.Path]::GetFullPath($offline).StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Offline cleanup escaped dev temp.' }
            Remove-Item -LiteralPath $offline -Recurse -Force
        }
        foreach ($package in $index.Packages) { $package.Enabled = $package.PackageId -eq $selected[0].PackageId }
        $settings.Client = 'lunar'
        $settings.MinecraftVersion = '1.8.9'
        $settings.CloseAfterLaunch = $false
        $settings.DisabledMods = @($index.Packages | Where-Object { $_.Kind -eq 0 -and -not $_.Enabled } | ForEach-Object OriginalFileName)
        $settings.DisabledAgents = @($index.Packages | Where-Object Kind -eq 1 | ForEach-Object OriginalFileName)
        [IO.File]::WriteAllText($indexPath, ($index | ConvertTo-Json -Depth 50), $utf8)
        [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 50), $utf8)
        @{
            Backend='OfficialLunar'; Loader='1.3.4'; BwhSha256=$expected; EnabledMods=1; EnabledPackageAgents=0
            TechnicalAgents=$agents; Runtime='pending-manual-launch'; Arguments=@('--dev-jvm-startup-diagnostic')
            ExecutableSha256=(Get-FileHash -LiteralPath $executable).Hash
            PreparedIndexSha256=(Get-FileHash -LiteralPath $indexPath).Hash
            PreparedSettingsSha256=(Get-FileHash -LiteralPath $settingsPath).Hash
            Capture='existing sanitized native stdout/stderr tee; 2 MiB per stream; no single-mod smoke mode'
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Encoding utf8
    } catch { Restore-Selection $evidence; throw }
}
Write-Host "Prepared ONLY BWH via normal OfficialLunar. Evidence: $evidence"
if ($PrepareOnly) { return }
$previous = [Environment]::GetEnvironmentVariable('MOONRISE_DEV_STARTUP_CAPTURE')
$game = $null
$runStarted = [DateTime]::UtcNow
$result = [ordered]@{ JvmState='not-observed'; JvmExitCode=$null; Runtime='requires-user-and-log-review' }
try {
    $env:MOONRISE_DEV_STARTUP_CAPTURE = $evidence
    # This flag only enables the existing output capture; normal launch selection is unchanged.
    $moonrise = Start-Process -FilePath $executable -ArgumentList '--dev-jvm-startup-diagnostic' -WorkingDirectory $evidence -WindowStyle Hidden -PassThru
    Write-Host 'Click Launch manually in Moonrise. After testing close Minecraft normally, then exit Moonrise (including its tray icon). Keep this console open.'
    while (-not $moonrise.HasExited) {
        if (-not $game -and $result.JvmState -eq 'not-observed') {
            $capture = Get-ChildItem -LiteralPath $evidence -Filter 'jvm-*-stdout.log' | Select-Object -First 1
            if ($capture) {
                $gameId = [int]([regex]::Match($capture.Name, 'jvm-(\d+)-').Groups[1].Value)
                try { $game = [Diagnostics.Process]::GetProcessById($gameId); $null = $game.Handle; $result.JvmState='running' }
                catch { $result.JvmState='exited-before-monitor-attached' }
            }
        }
        if ($game -and $game.HasExited) { $result.JvmState='exited'; $result.JvmExitCode=$game.ExitCode }
        Start-Sleep -Milliseconds 200
    }
} finally {
    [Environment]::SetEnvironmentVariable('MOONRISE_DEV_STARTUP_CAPTURE', $previous)
    if ($game -and $game.HasExited) { $result.JvmState='exited'; $result.JvmExitCode=$game.ExitCode }
    $result.BwhSha256After = (Get-FileHash -LiteralPath $modPath).Hash
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    # These reports are sanitized by Moonrise; never collect official launcher/account files.
    Get-ChildItem -LiteralPath (Join-Path $dataRoot 'logs') -Filter 'launch-*.json' -File |
        Where-Object { $_.LastWriteTimeUtc -ge $runStarted } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $evidence $_.Name) }
    if (-not (Get-Process Moonrise -ErrorAction SilentlyContinue)) { Restore-Selection $evidence }
    else { Write-Host 'Moonrise still open: close it, then run this script with -RestoreOnly.' }
    Write-Host "Evidence retained: $evidence"
}
