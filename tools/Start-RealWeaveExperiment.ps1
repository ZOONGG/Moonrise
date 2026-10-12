param([ValidateSet('Bwh','Mixed')][string]$Scenario = 'Bwh', [switch]$PrepareOnly, [string]$BinaryDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$binary = if ($BinaryDirectory) { [IO.Path]::GetFullPath($BinaryDirectory) } else { Join-Path $root 'runtime/mixed-real-validation/bin/Moonrise/release_win-x64' }
$executable = Join-Path $binary 'Moonrise.exe'
$base = Join-Path $env:LOCALAPPDATA 'Moonrise'
$name = if ($Scenario -eq 'Bwh') { 'bwh-baseline' } else { 'raven-bwh' }
$data = Join-Path $base "dev/experiments/$name"
$flag = if ($Scenario -eq 'Bwh') { '--dev-bwh-real-baseline' } else { '--dev-experimental-raven-bwh' }
if (-not $PrepareOnly -and (Get-Process Moonrise,javaw -ErrorAction SilentlyContinue)) { throw 'Close Moonrise and Minecraft before a new experiment.' }
[void][Reflection.Assembly]::LoadFrom((Join-Path $binary 'Moonrise.dll'))
$paths = [Moonrise.Infrastructure.AppPaths]::new($data, $binary, [Moonrise.Infrastructure.AppMode]::Development)
$paths.EnsureUserDirectories()
$settings = Join-Path $data 'settings/moonrise-settings.json'
if (-not (Test-Path -LiteralPath $settings)) {
    Copy-Item -LiteralPath (Join-Path $base 'settings/moonrise-settings.json') -Destination $settings
}
$parser = [Moonrise.Services.JarMetadataParser]::new()
$library = [Moonrise.Services.LocalPackageLibrary]::new($paths, $parser)
$library.Load()
$bwh = Join-Path $base 'adapters/maintained-builds/6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57.jar'
$inputs = @($bwh)
if ($Scenario -eq 'Mixed') { $inputs += Join-Path $base 'packages/weave/RavenWeave-1.1.4.jar' }
foreach ($inputJar in $inputs) {
    $result = $library.Import($inputJar)
    if (-not $result.Package) { throw "Import failed: $($result.Status)" }
}
foreach ($package in $library.Packages) { $library.SetEnabled($package, $true) }
if ($library.Packages.Count -ne $inputs.Count) { throw 'Unexpected package in isolated experiment library.' }
if ($Scenario -eq 'Mixed') { [Moonrise.Services.ExperimentalMixedWeaveAdapter]::ValidateSelection([Moonrise.Models.PackageInfo[]]@($library.Packages), [Moonrise.Models.PackageInfo[]]@()) }
else {
    if ($library.Packages[0].Sha256 -ne [Moonrise.Services.ExperimentalMixedWeaveAdapter]::BwhHash) { throw 'Baseline BWH mismatch.' }
}
$loader = Join-Path $base 'dev/cache/runtime/weave/weave-loader.jar'
if ((Get-FileHash -LiteralPath $loader).Hash -ne '013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7') { throw 'Loader mismatch.' }
Copy-Item -LiteralPath $loader -Destination $paths.WeaveAgentPath
if ($Scenario -eq 'Mixed') {
    # Exercise actual session staging before opening the app, without starting Lunar.
    $staging = Join-Path $paths.TempDirectory ('mixed-preflight-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        $mods = [Moonrise.Models.PackageInfo[]]@($library.Packages)
        $enabledDirectory = [Moonrise.Services.EnabledModDirectoryService]::new($parser).Create($staging, $mods)
        $plan = [Moonrise.Services.CurrentLaunchPlanFactory]::new().Create($mods, [Moonrise.Models.PackageInfo[]]@(), $paths.WeaveAgentPath, $false)
        $adapter = Join-Path $root 'runtime/mixed-weave-adapter/moonrise-mixed-agent-0.1.0.jar'
        $plan = [Moonrise.Services.ExperimentalMixedWeaveAdapter]::Prepare($plan, $enabledDirectory, $adapter)
        $plan = [Moonrise.Services.CurrentWeaveSyntheticAdapter]::Prepare($plan, $staging)
        $null = [Moonrise.Services.NativeBridgeConfigurationBuilder]::BuildLaunchPlan($plan, $enabledDirectory)
        if (@($plan.Agents | Where-Object IsWeaveLoader).Count -ne 1) { throw 'Competing loader in mixed plan.' }
        Write-Host 'Mixed session staging/hash verification/MNR4 serialization passed; runtime unverified.'
    } finally {
        $tempBoundary = [IO.Path]::GetFullPath($paths.TempDirectory) + [IO.Path]::DirectorySeparatorChar
        if (-not [IO.Path]::GetFullPath($staging).StartsWith($tempBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging cleanup escaped experiment temp.' }
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
$settingsService = [Moonrise.Services.AppSettingsService]::new($settings)
$config = $settingsService.Load()
$config.Client = 'lunar'; $config.MinecraftVersion = '1.8.9'; $config.CloseAfterLaunch = $false
$config.DisabledMods = @(); $config.DisabledAgents = @()
$settingsService.Save($config)
$evidence = Join-Path $paths.LogsDirectory ('real-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$captureBridge = Join-Path $root 'runtime/mapping-startup-build/Moonrise.Native.dll'
if ((Get-FileHash -LiteralPath $captureBridge).Hash -ne '0A1583307A742A7456DF7657D21EB59A7F7157235D3532C53737BD85E43E9775') { throw 'Diagnostic native bridge hash mismatch.' }
Copy-Item -LiteralPath $captureBridge -Destination (Join-Path $evidence 'Moonrise.Native.dll')
@{ Scenario=$Scenario; DataRoot=$data; ModHashes=@($library.Packages | ForEach-Object Sha256); Status='prepared-not-launched' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'preparation.json')
Write-Host "Prepared $Scenario with $($inputs.Count) exact mod(s); separate data root: $data"
if ($PrepareOnly) { return }
$previousCapture = $env:MOONRISE_DEV_STARTUP_CAPTURE
$previousAdapter = $env:MOONRISE_MIXED_ADAPTER
try {
    $env:MOONRISE_DEV_STARTUP_CAPTURE = $evidence
    if ($Scenario -eq 'Mixed') { $env:MOONRISE_MIXED_ADAPTER = Join-Path $root 'runtime/mixed-weave-adapter/moonrise-mixed-agent-0.1.0.jar' }
    $process = Start-Process -FilePath $executable -ArgumentList @($flag, '--dev-jvm-startup-diagnostic') -WorkingDirectory $evidence -WindowStyle Hidden -PassThru
    Write-Host "Click Launch once. Evidence: $evidence"
    while (-not $process.HasExited) { Start-Sleep -Milliseconds 500 }
    @{ MoonriseExitCode=$process.ExitCode; OriginalHashesAfter=@($inputs | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }); Status='requires-runtime-log-and-functional-review' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json')
} finally {
    $env:MOONRISE_DEV_STARTUP_CAPTURE = $previousCapture
    $env:MOONRISE_MIXED_ADAPTER = $previousAdapter
}
