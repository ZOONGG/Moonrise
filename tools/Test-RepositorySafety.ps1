param(
    [string]$PrivatePatterns = $env:MOONRISE_PRIVATE_PATTERNS
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
    $tracked = @(git ls-files --cached --others --exclude-standard) | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate repository files." }

    $approvedArtifacts = @{
        'runtime/main-cosmetics-build/moonrise-cosmetics-1.3.2.jar' = 'EFBAC1DC8F6B28EE1A13E294FF7DC03880C384306F1FFAB88738E3DCD1DC4354'
        'runtime/mixed-weave-adapter/moonrise-mixed-agent-0.1.0.jar' = 'B1E5408C014486731E24B56EFCD7B32F2219249A17ACCACA9F897270C2EB70E4'
        'runtime/mixed-weave-adapter/moonrise-mixed-raven-abi-0.1.0.jar' = 'F73CDA328C5538BE38316207C184B3D6734E0A63FEBA0F958E9CE57304C473BC'
        'runtime/catalog/Veyra-0.1.1-moonrise.1.jar' = '779760392BCA1F269D0EB325E78950249D355E94A09C90988108C3CCD94D95BD'
        'extensions/MixedWeave/Weave-Loader-0.2.6-source.zip' = 'FE9EFB97F292DEB931923094F755D1EF59E8C6696B246CED0A32497648D1F711'
    }
    foreach ($file in $approvedArtifacts.Keys) {
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $approvedArtifacts[$file]) {
            throw "Reviewed artifact missing or hash mismatch: $file"
        }
    }
    $forbidden = $tracked | Where-Object {
        $_ -match '^(user-mods|user-agents|profiles|plugins|language-packs|themes|logs|crash-reports|release|artifacts?)(/|$)' -or
        $_ -match '(?i)(^|/)(account|accounts|session|sessions|token|tokens|credential|credentials|settings)(\.[^/]*)?$' -or
        ($_ -match '(?i)\.(jar|pfx|p12|pem|key|snk|log|dmp|mdmp|zip|7z|rar)$' -and
            $_ -ne 'runtime/adapters/moonrise-bwh-exitlag-network-agent.jar' -and -not $approvedArtifacts.ContainsKey($_)) -or
        ($_ -match '(?i)\.(exe|dll)$' -and $_ -ne 'runtime/bridge/Moonrise.Native.dll')
    }
    if ($forbidden) {
        $forbidden | ForEach-Object { Write-Error "Forbidden tracked path: $_" }
        throw "Repository safety scan found forbidden tracked files."
    }

    $patterns = @($PrivatePatterns -split '[,;\r\n]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $secretSignature = '(gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|sk-(?:proj-)?[A-Za-z0-9_-]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|eyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,})'
    foreach ($file in $tracked) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or
            $file -match '(?i)\.(dll|exe|jar|png|ico|jpg|jpeg|gif|woff2?|ttf|zip|pdf)$') { continue }
        $content = [IO.File]::ReadAllText((Join-Path $repository $file))
        if ($content -match $secretSignature) {
            throw "Potential secret signature in $file (value withheld)."
        }
        if ($file -eq 'tools/Test-RepositorySafety.ps1') { continue }
        foreach ($pattern in $patterns) {
            if ($content.IndexOf($pattern, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Configured private-name pattern matched in $file (value withheld)."
            }
        }
    }

    Write-Host "Repository safety scan passed for $($tracked.Count) tracked/untracked files."
}
finally {
    Pop-Location
}
