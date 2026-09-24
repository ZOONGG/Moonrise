param(
    [string]$PrivatePatterns = $env:MOONRISE_PRIVATE_PATTERNS
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
    $tracked = @(git ls-files)
    if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate tracked files." }

    $forbidden = $tracked | Where-Object {
        $_ -match '^(user-mods|user-agents|profiles|plugins|language-packs|themes|logs|crash-reports|release|artifacts?)(/|$)' -or
        $_ -match '(?i)(^|/)(account|accounts|session|sessions|token|tokens|credential|credentials|settings)(\.[^/]*)?$' -or
        ($_ -match '(?i)\.(jar|pfx|p12|pem|key|snk|log|dmp|mdmp|zip|7z|rar)$' -and
            $_ -ne 'runtime/adapters/moonrise-bwh-exitlag-network-agent.jar') -or
        ($_ -match '(?i)\.(exe|dll)$' -and $_ -ne 'runtime/bridge/Moonrise.Native.dll')
    }
    if ($forbidden) {
        $forbidden | ForEach-Object { Write-Error "Forbidden tracked path: $_" }
        throw "Repository safety scan found forbidden tracked files."
    }

    $patterns = @($PrivatePatterns -split '[,;\r\n]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($pattern in $patterns) {
        git grep -I -n -i -F -- $pattern -- . ':!tools/Test-RepositorySafety.ps1' | Out-Null
        if ($LASTEXITCODE -eq 0) { throw "Repository content matched a configured private-name pattern." }
        if ($LASTEXITCODE -gt 1) { throw "Private-name scan failed." }
    }

    Write-Host "Repository safety scan passed for $($tracked.Count) tracked files."
}
finally {
    Pop-Location
}
