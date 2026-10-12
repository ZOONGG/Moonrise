param([string]$OutputRoot = 'artifacts/installed-update-smoke')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = [IO.Path]::GetFullPath((Join-Path $repo $OutputRoot))
if (Test-Path -LiteralPath $root) { throw 'Output already exists; use a new OutputRoot to preserve artifacts.' }
$vpk = Join-Path $repo 'artifacts/velopack-tools/vpk.exe'
if (-not (Test-Path -LiteralPath $vpk)) {
    dotnet tool install vpk --version 1.2.161 --tool-path (Split-Path $vpk)
    if ($LASTEXITCODE) { throw 'vpk install failed' }
}
dotnet test (Join-Path $repo 'tests/Moonrise.Tests/Moonrise.Tests.csproj') -c Release --filter 'FullyQualifiedName~UpdateServiceTests|FullyQualifiedName~UpdateUiStateTests'
if ($LASTEXITCODE) { throw 'Targeted tests failed' }
foreach ($number in 1,2) {
    & "$PSScriptRoot/New-MoonriseRelease.ps1" -Version "0.1.0-beta.$number" -OutputRoot "$root/beta$number" -Velopack -VpkPath $vpk -LocalUpdateFeed "$root/feed" -SkipTests
}
New-Item -ItemType Directory -Path "$root/feed" | Out-Null
Copy-Item "$root/beta2/velopack/*full.nupkg", "$root/beta2/velopack/releases.beta.json", "$root/beta2/velopack/RELEASES-beta" "$root/feed"
Write-Output "Local-only feed: $root/feed (do not move; embedded in both builds)."
