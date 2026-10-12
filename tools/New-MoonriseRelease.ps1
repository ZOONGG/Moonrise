param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-beta\.\d+)?$')]
    [string]$Version,

    [string]$OutputRoot = "release",
    [string]$DotNetPath = "dotnet",
    [string]$IsccPath = "",
    [switch]$SkipRestore,
    [switch]$SkipTests,
    [switch]$Velopack,
    [string]$VpkPath = "",
    [string]$LocalUpdateFeed = ""
)

$ErrorActionPreference = "Stop"
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$output = if ([IO.Path]::IsPathRooted($OutputRoot)) {
    [IO.Path]::GetFullPath($OutputRoot)
} else {
    [IO.Path]::GetFullPath((Join-Path $repository $OutputRoot))
}
$repositoryPrefix = $repository.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must stay inside the Moonrise repository."
}
if ($output -eq $repository) {
    throw "OutputRoot cannot be the repository root."
}

if (-not $Velopack -and [string]::IsNullOrWhiteSpace($IsccPath)) {
    $isccCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    $IsccPath = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Velopack -and ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath))) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found."
}

Push-Location $repository
try {
    if (Test-Path -LiteralPath $output) {
        $resolvedOutput = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $output))
        if (-not $resolvedOutput.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            $resolvedOutput -eq $repository) {
            throw "Refusing to remove unsafe release output path: $resolvedOutput"
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
    New-Item -ItemType Directory -Path $output | Out-Null

    if (-not $SkipRestore) {
        & $DotNetPath restore Moonrise.sln --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
    }
    if (-not $SkipTests) {
        & $DotNetPath test Moonrise.sln -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
    }

    $publish = Join-Path $output "Moonrise"
    $fourPartVersion = ($Version -split '-')[0] + '.0'
    & $DotNetPath publish src/Moonrise/Moonrise.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        --no-restore `
        -p:Version=$Version `
        -p:FileVersion=$fourPartVersion `
        -p:AssemblyVersion=$fourPartVersion `
        -p:InformationalVersion=$Version `
        -p:IncludeSourceRevisionInInformationalVersion=false `
        "-p:MoonriseLocalUpdateFeed=$LocalUpdateFeed" `
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    # Only these reproducible Moonrise runtime artifacts may accompany the EXE.
    # Never admit imported originals or arbitrary JARs by extension alone.
    $runtimeJars = @{
        'runtime\catalog\Veyra-0.1.1-moonrise.1.jar' = '779760392BCA1F269D0EB325E78950249D355E94A09C90988108C3CCD94D95BD'
        'runtime\maintained-builds\moonrise-cosmetics-1.3.2.jar' = 'EFBAC1DC8F6B28EE1A13E294FF7DC03880C384306F1FFAB88738E3DCD1DC4354'
        'runtime\mixed-weave-adapter\moonrise-mixed-agent-0.1.0.jar' = 'B1E5408C014486731E24B56EFCD7B32F2219249A17ACCACA9F897270C2EB70E4'
        'runtime\mixed-weave-adapter\moonrise-mixed-raven-abi-0.1.0.jar' = 'F73CDA328C5538BE38316207C184B3D6734E0A63FEBA0F958E9CE57304C473BC'
    }
    $forbidden = @(Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object {
        $relative = [IO.Path]::GetRelativePath($publish, $_.FullName)
        ($_.Extension -eq '.jar' -and -not $runtimeJars.ContainsKey($relative)) -or
        ($_.Extension -eq '.zip' -and $relative -ne 'licenses\mixed-weave\Weave-Loader-0.2.6-source.zip') -or
        $_.Extension -in ".log", ".dmp", ".mdmp", ".pdb", ".pfx", ".p12", ".pem", ".key" -or
        $_.Name -match "(?i)(account|credential|password|session|settings|token)"
    })
    if ($forbidden.Count -gt 0) {
        $forbidden | ForEach-Object { Write-Error "Forbidden publish entry: $($_.FullName)" }
        throw "Release safety check failed."
    }
    foreach ($relative in $runtimeJars.Keys) {
        $jar = Join-Path $publish $relative
        if (-not (Test-Path -LiteralPath $jar -PathType Leaf) -or
            (Get-FileHash -LiteralPath $jar -Algorithm SHA256).Hash -ne $runtimeJars[$relative]) {
            throw "Required runtime JAR is missing or has an unexpected SHA-256: $relative"
        }
    }
    $sourceArchive = Join-Path $publish 'licenses/mixed-weave/Weave-Loader-0.2.6-source.zip'
    if (-not (Test-Path -LiteralPath $sourceArchive) -or
        (Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash -ne 'FE9EFB97F292DEB931923094F755D1EF59E8C6696B246CED0A32497648D1F711') {
        throw 'Mixed adapter corresponding source archive is missing or has an unexpected hash.'
    }
    foreach ($required in "Moonrise.exe", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md", "runtime\bridge\Moonrise.Native.dll") {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $required))) {
            throw "Required release entry is missing: $required"
        }
    }

    if ($Velopack) {
        if (-not (Test-Path -LiteralPath $VpkPath)) { throw "VpkPath must point to pinned vpk 1.2.161." }
        if ($Version -notmatch '-beta\.\d+$') { throw 'The current updater requires a beta.N version.' }
        $packId = if ($LocalUpdateFeed) { 'Moonrise.UpdateSmoke.App' } else { 'Moonrise.App' }
        $packTitle = if ($LocalUpdateFeed) { 'Moonrise Update Smoke' } else { 'Moonrise' }
        & $VpkPath pack --packId $packId --packVersion $Version --packDir $publish `
            --mainExe Moonrise.exe --channel beta --runtime win-x64 --delta None `
            --packTitle $packTitle --packAuthors ZOONGG `
            --releaseNotes (Join-Path $repository 'docs/RELEASE_NOTES.md') `
            --icon (Join-Path $repository 'assets/branding/moonrise-icon.ico') --outputDir (Join-Path $output 'velopack')
        if ($LASTEXITCODE -ne 0) { throw "Velopack packaging failed." }
        $assets = @(Get-ChildItem -LiteralPath (Join-Path $output 'velopack') -File)
        foreach ($asset in $assets) {
            $hash = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            Set-Content -LiteralPath "$($asset.FullName).sha256" -Value "$hash  $($asset.Name)" -Encoding ascii
        }
        return
    }
    $portableStage = Join-Path $output ".portable-stage"
    Copy-Item -LiteralPath $publish -Destination $portableStage -Recurse
    New-Item -ItemType File -Path (Join-Path $portableStage "Moonrise.portable") | Out-Null
    $portableName = "Moonrise-Portable-$Version-x64.zip"
    $portablePath = Join-Path $output $portableName
    & (Join-Path $PSScriptRoot "New-DeterministicArchive.ps1") `
        -SourceDirectory $portableStage `
        -DestinationPath $portablePath
    if ($LASTEXITCODE -ne 0) { throw "Portable archive creation failed." }
    Remove-Item -LiteralPath $portableStage -Recurse -Force

    & $IsccPath `
        "/DAppVersion=$Version" `
        "/DSourceDir=$publish" `
        "/DOutputDir=$output" `
        (Join-Path $repository "installer\Moonrise.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

    $setupName = "Moonrise-Setup-$Version-x64.exe"
    $setupPath = Join-Path $output $setupName
    if (-not (Test-Path -LiteralPath $setupPath)) {
        throw "Expected installer was not produced: $setupPath"
    }

    foreach ($artifact in @($setupPath, $portablePath)) {
        $name = Split-Path -Leaf $artifact
        $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -LiteralPath "$artifact.sha256" -Value "$hash  $name" -Encoding ascii -NoNewline
    }

    $notesName = "Moonrise-Release-Notes-$Version.md"
    Copy-Item -LiteralPath (Join-Path $repository "docs\RELEASE_NOTES.md") -Destination (Join-Path $output $notesName)

    [pscustomobject]@{
        Setup = $setupPath
        SetupChecksum = "$setupPath.sha256"
        Portable = $portablePath
        PortableChecksum = "$portablePath.sha256"
        ReleaseNotes = Join-Path $output $notesName
        PublishDirectory = $publish
    } | Format-List
}
finally {
    Pop-Location
}
