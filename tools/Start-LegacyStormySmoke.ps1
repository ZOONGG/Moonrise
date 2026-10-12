param([switch]$PrepareOnly, [string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $appRoot 'src/Moonrise/bin/Release/net8.0-windows/win-x64/Moonrise.exe'
if ($ExecutablePath) { $executable = [IO.Path]::GetFullPath($ExecutablePath) }
$dataRoot = Join-Path $env:LOCALAPPDATA 'Moonrise/dev'
$loader = Join-Path $dataRoot 'cache/runtime/weave/weave-loader-0.2.6.jar'
if (Get-Process Moonrise -ErrorAction SilentlyContinue) {
    throw 'Close Moonrise first so it cannot overwrite the smoke package configuration.'
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Build the smoke executable first.' }
$bridge = Join-Path $appRoot 'runtime/mapping-startup-build/Moonrise.Native.dll'
if (-not (Test-Path -LiteralPath $bridge -PathType Leaf)) { throw 'Startup capture bridge is missing.' }
if ((Get-FileHash -LiteralPath $loader -Algorithm SHA256).Hash -ne
    'C4200D145C0D3D78F57EAB4DDEE3A64FF1A3176E65F2E2B15A31777A0142B524') {
    throw 'Legacy loader hash mismatch.'
}
# Legacy 0.2.6 uses user.home/.weave/mods, ignoring weave.mods.directory.
# Normal legacy launch uses the existing session directory adapter.
$legacyMods = Join-Path $env:USERPROFILE '.weave/mods'
if (Get-ChildItem -LiteralPath $legacyMods -Filter '*.jar' -File -Recurse -ErrorAction SilentlyContinue) {
    throw 'Legacy ~/.weave/mods contains JARs. Remove external selection ambiguity before this smoke.'
}
foreach ($variableName in @('JAVA_TOOL_OPTIONS', '_JAVA_OPTIONS', 'JDK_JAVA_OPTIONS', 'MOONRISE_MIXIN_DIAGNOSTIC_AGENT', 'MOONRISE_MIXIN_DIAGNOSTIC_OPTIONS')) {
    if ([Environment]::GetEnvironmentVariable($variableName)) {
        throw "Clear $variableName for this smoke; inherited JVM options would invalidate isolation."
    }
}
$indexPath = Join-Path $dataRoot 'packages/metadata/index.json'
$settingsPath = Join-Path $dataRoot 'settings/moonrise-settings.json'
$index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$evidenceDirectory = Join-Path $dataRoot ('logs/legacy-stormy-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDirectory | Out-Null
# Only Moonrise package/settings files are backed up; no Lunar account files.
Copy-Item -LiteralPath $indexPath -Destination (Join-Path $evidenceDirectory 'package-index.before.json')
Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $evidenceDirectory 'moonrise-settings.before.json')
$stormy = @($index.Packages | Where-Object { $_.Kind -eq 0 -and $_.Sha256 -eq '52ED9DA4C4F570FD43B32671E0D94FA5FC9E441168645D2E3DE054169374B021' })
if ($stormy.Count -ne 1) { throw 'Expected exactly one original Stormy package in the dev library.' }
$stormyPath = Join-Path $dataRoot $stormy[0].ManagedFilePath
if ((Get-FileHash -LiteralPath $stormyPath -Algorithm SHA256).Hash -ne $stormy[0].Sha256) { throw 'Stormy integrity failure.' }
foreach ($package in $index.Packages) { $package.Enabled = $package.PackageId -eq $stormy[0].PackageId }
$settings.Client = 'lunar'
$settings.MinecraftVersion = '1.8.9'
$settings.CloseAfterLaunch = $false
$settings.DisabledMods = @($index.Packages | Where-Object { $_.Kind -eq 0 -and -not $_.Enabled } | ForEach-Object OriginalFileName)
$settings.DisabledAgents = @($index.Packages | Where-Object Kind -eq 1 | ForEach-Object OriginalFileName)
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($indexPath, ($index | ConvertTo-Json -Depth 50), $utf8)
[IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 50), $utf8)
@{
    PreparedAtUtc = [DateTime]::UtcNow.ToString('o')
    LoaderVersion = '0.2.6'
    LoaderPath = $loader
    LoaderSha256 = (Get-FileHash -LiteralPath $loader -Algorithm SHA256).Hash
    EnabledMods = 1
    EnabledPackageAgents = 0
    Backend = 'OfficialLunar'
    StormyPath = $stormyPath
    StormySha256 = $stormy[0].Sha256
    RuntimeSmoke = 'pending-user-launch'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'preparation.json') -Encoding UTF8
Copy-Item -LiteralPath $bridge -Destination (Join-Path $evidenceDirectory 'Moonrise.Native.dll')
if (-not $PrepareOnly) {
    $previousCapture = [Environment]::GetEnvironmentVariable('MOONRISE_DEV_STARTUP_CAPTURE')
    try {
        $env:MOONRISE_DEV_STARTUP_CAPTURE = $evidenceDirectory
        $moonrise = Start-Process -FilePath $executable -ArgumentList '--dev-weave-mod-smoke','--dev-jvm-startup-diagnostic' -WorkingDirectory $evidenceDirectory -WindowStyle Hidden -PassThru
    } finally { [Environment]::SetEnvironmentVariable('MOONRISE_DEV_STARTUP_CAPTURE', $previousCapture) }
}
Write-Host 'Prepared: OfficialLunar, legacy Weave 0.2.6, ONLY Stormy, zero package agents. Click Launch in Moonrise after confirmation.'
Write-Host "Preparation evidence: $evidenceDirectory"

if ($PrepareOnly) { return }
Write-Host 'Click Launch once. This console collects evidence; after testing close Minecraft normally.'
$result = [ordered]@{ XsrgVerified=$false; ServiceCreated=$false; ServiceSelected=$false; InitComplete=$false; JvmState='waiting-for-manual-launch'; JvmExitCode=$null; Menu='pending-user-confirmation'; FirstException=$null; Stage='waiting-for-manual-launch'; StormyDetected=$false; InitializerReturned=$false; ConfigPrepared=$false; MixinsObserved=@(); LateMixinWarning=$false }
$game = $null
$missedExit = $false
while ($true) {
    $mappingLog = Get-ChildItem -LiteralPath $evidenceDirectory -Filter 'jvm-*-mapping.log' | Select-Object -First 1
    if ($mappingLog -and -not $game) {
        $gameId = [int]([regex]::Match($mappingLog.Name,'jvm-(\d+)-').Groups[1].Value)
        try { $game = [Diagnostics.Process]::GetProcessById($gameId); $null = $game.Handle }
        catch { $result.JvmState = 'exited-before-monitor-attached'; $missedExit = $true }
    }
    $lines = @(Get-ChildItem -LiteralPath $evidenceDirectory -Filter 'jvm-*.log' | ForEach-Object { Get-Content -LiteralPath $_.FullName -ErrorAction SilentlyContinue })
    $text = $lines -join "`n"
    $result.XsrgVerified = $text.Contains('genesis-resource-attached-verified')
    $result.ServiceCreated = $text.Contains('[Moonrise Weave] service-created')
    $result.ServiceSelected = $text.Contains('service-selected=net.weavemc.loader.mixins.WeaveMixinService')
    $result.InitComplete = $text.Contains('[Moonrise Weave] init-complete')
    $result.StormyDetected = $text -match '\[Weave\] Loading .*Stormy-1\.0\.jar'
    # Pinned loader bytecode invokes every configured preInit synchronously before init-complete.
    # With one hash-verified mod, detection + init-complete proves Main.preInit returned.
    $result.InitializerReturned = $result.StormyDetected -and $result.InitComplete
    $result['ConfigRegistered'] = $result.StormyDetected -and $result.InitComplete
    $result.ConfigPrepared = $text -match '(?im)(Preparing|Selecting|prepared).*stormy\.mixins\.json'
    $result.MixinsObserved = @([regex]::Matches($text, '(?im)^.*Mixing .* from stormy\.mixins\.json into .*') | ForEach-Object Value | Select-Object -Unique)
    $result.LateMixinWarning = $text -match '(?i)Some mixins may be skipped|initiali[sz].*late|late.*initiali[sz]'
    $result.Stage = if ($result.MixinsObserved.Count) { 'stormy-mixin-processing-observed' } elseif ($result.InitializerReturned) { 'stormy-preInit-returned' } elseif ($result.ConfigPrepared) { 'stormy-config-prepared' } elseif ($result.StormyDetected) { 'stormy-discovered' } elseif ($result.ServiceSelected) { 'mixin-selected' } elseif ($result.ServiceCreated) { 'mixin-created' } elseif ($result.XsrgVerified) { 'xsrg-verified' } else { 'mapping-bootstrap' }
    if (-not $result.FirstException) {
        $first = $lines | Where-Object { $_ -match '^(?:java\.|org\.|net\.|com\.)[\w.$]+(?:Exception|Error)(?::|$)|Exception in thread|FAIL CLOSED:' } | Select-Object -First 1
        if ($first) {
            $result.FirstException = $first
            @("Stage: $($result.Stage)", $first, $text) | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'first-exception.txt') -Encoding UTF8
        }
    }
    if ($game) {
        $result.JvmState = if ($game.HasExited) { 'exited' } else { 'running' }
        if ($game.HasExited) { $result.JvmExitCode = $game.ExitCode }
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'result.json') -Encoding UTF8
    if ($missedExit -or ($game -and $game.HasExited)) { break }
    if (-not $game -and $moonrise.HasExited) { break }
    Start-Sleep -Milliseconds 200
}
$result['StormySha256After'] = (Get-FileHash -LiteralPath $stormyPath -Algorithm SHA256).Hash
$result['MixinApplication'] = 'requires-exported-bytecode-review; Mixing lines alone are processing evidence'
$result['StormyClientInit'] = 'requires-runtime-review; preInit return proves event subscription only'
Write-Host 'Menu alone does not prove Stormy works. Review config preparation, Mixing lines, exceptions and late initialization warning.'
$result.Menu = if ((Read-Host 'Did Minecraft reach a working main menu? [y/N]') -match '^(y|yes)$') { 'confirmed-by-user' } else { 'not-confirmed' }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'result.json') -Encoding UTF8
Write-Host "Evidence saved: $evidenceDirectory"
