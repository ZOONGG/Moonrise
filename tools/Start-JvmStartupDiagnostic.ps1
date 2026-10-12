param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $appRoot 'runtime/startup-release/framework/Release/net8.0-windows/win-x64/Moonrise.exe'
$bridge = Join-Path $appRoot 'runtime/mapping-startup-build/Moonrise.Native.dll'
if (!(Test-Path -LiteralPath $executable) -or !(Test-Path -LiteralPath $bridge)) { throw 'Build the diagnostic Release executable and developer bridge first.' }
if (!$PrepareOnly -and (Get-Process Moonrise -ErrorAction SilentlyContinue)) { throw 'Close Moonrise before this manual smoke; its package/settings configuration must remain unchanged.' }
$directory = Join-Path $env:LOCALAPPDATA ('Moonrise/dev/logs/jvm-startup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $directory | Out-Null
Copy-Item -LiteralPath $bridge -Destination (Join-Path $directory 'Moonrise.Native.dll')
$nativeLog = Join-Path $env:LOCALAPPDATA "Moonrise/logs/native-bridge.log"
@{
    NativeLogStartOffset = $(if (Test-Path -LiteralPath $nativeLog) { (Get-Item -LiteralPath $nativeLog).Length } else { 0 })
    PreparedAtUtc = [DateTime]::UtcNow.ToString('o')
    Backend = 'OfficialLunar'
    NativeBridgeSha256 = (Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash
    ExecutableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    State = 'pending-manual-game-launch'
    Capture = 'native CreateProcess stdout/stderr tee; sanitized; 2 MiB per stream; flushed per line and at EOF'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'preparation.json') -Encoding utf8
$previous = [Environment]::GetEnvironmentVariable('MOONRISE_DEV_STARTUP_CAPTURE')
try {
    $env:MOONRISE_DEV_STARTUP_CAPTURE = $directory
    if (!$PrepareOnly) {
        Start-Process -FilePath $executable -ArgumentList '--dev-legacy-weave-smoke', '--dev-jvm-startup-diagnostic' -WindowStyle Hidden
    }
} finally {
    [Environment]::SetEnvironmentVariable('MOONRISE_DEV_STARTUP_CAPTURE', $previous)
}
Write-Host "Evidence: $directory"
Write-Host 'Existing settings/packages unchanged. In Moonrise verify OfficialLunar / 1.8.9 / Legacy Weave 0.2.6, then click Launch exactly once.'
if ($PrepareOnly) { Write-Host 'PrepareOnly: Moonrise was not opened. Run this script without PrepareOnly when ready.' }
