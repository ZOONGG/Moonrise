param([string]$ReportRoot = 'artifacts/user-data-audit')
$ErrorActionPreference = 'Stop'
$main = Join-Path $env:LOCALAPPDATA 'Moonrise'
$out = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) $ReportRoot))
if (Test-Path -LiteralPath $out) { throw 'Report directory already exists; choose a new ReportRoot.' }
New-Item -ItemType Directory -Path $out | Out-Null
# Only package JARs and recognized metadata; never traverse account stores.
$roots = @($main) + @(Get-ChildItem -LiteralPath $main -Directory | Where-Object { $_.Name -eq 'dev' -or $_.Name -like 'app-*' } | ForEach-Object FullName)
$rows = @()
foreach ($root in $roots) {
    $packages = Join-Path $root 'packages'
    if (!(Test-Path -LiteralPath $packages)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $packages -File -Recurse) {
        if ($file.FullName -match '(?i)token|cookie|account|credential|password|secret') { continue }
        if ($file.Extension -notin '.jar','.json','.txt','.log' -and $file.Name -notlike '.layout-*') { continue }
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse point requires manual inspection.' }
        $relative = [IO.Path]::GetRelativePath($packages, $file.FullName)
        $target = Join-Path (Join-Path $main 'packages') $relative
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $status = 'unique-review-required'
        if ($root -eq $main) { $status = 'authoritative-preserve' }
        elseif (Test-Path -LiteralPath $target) {
            $status = if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $hash) { 'identical' } else { 'conflict-do-not-overwrite' }
        }
        $rows += [pscustomobject]@{ Source=$file.FullName; Destination=$target; SHA256=$hash; Status=$status }
        $backup = Join-Path $out (Join-Path ('backup/' + $(if ($root -eq $main) {'main'} else {Split-Path $root -Leaf})) $relative)
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $backup
        if ((Get-FileHash -LiteralPath $backup).Hash -ne $hash) { throw 'Backup verification failed.' }
    }
}
$rows | Export-Csv -LiteralPath (Join-Path $out 'dry-run.csv') -NoTypeInformation -Encoding utf8
Get-ChildItem -LiteralPath $main -Directory | ForEach-Object {
    $category = if ($_.Name -in 'packages','settings','adapters','cosmetics','plugins','cache','temp','logs','themes','dev') { 'preserve' } elseif ($_.Name -eq 'dev' -or $_.Name -like 'app-*') { 'review-before-confirmed-deletion' } else { 'unknown' }
    [pscustomobject]@{ Directory=$_.FullName; Classification=$category }
} | Export-Csv -LiteralPath (Join-Path $out 'cleanup.csv') -NoTypeInformation -Encoding utf8
$settingsRows = foreach ($root in $roots) {
    $source = Join-Path $root 'settings/moonrise-settings.json'
    if (Test-Path -LiteralPath $source) {
        [pscustomobject]@{ Source=$source; Destination=(Join-Path $main 'settings/moonrise-settings.json');
            Length=(Get-Item -LiteralPath $source).Length; SHA256=(Get-FileHash -LiteralPath $source).Hash;
            Action='preserve; compare manually; no migration' }
    }
}
$settingsRows | Export-Csv -LiteralPath (Join-Path $out 'settings-dry-run.csv') -NoTypeInformation -Encoding utf8
Write-Output $out
