param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$OutputRoot = "release",
    [string]$DotNetPath = "dotnet",
    [string]$IsccPath = "",
    [switch]$SkipRestore,
    [switch]$SkipTests
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

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $isccCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    $IsccPath = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath)) {
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
    $fourPartVersion = "$Version.0"
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
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $forbidden = @(Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object {
        $_.Extension -in ".jar", ".log", ".dmp", ".mdmp", ".zip" -or
        $_.Name -match "(?i)(account|credential|password|session|settings|token)"
    })
    if ($forbidden.Count -gt 0) {
        $forbidden | ForEach-Object { Write-Error "Forbidden publish entry: $($_.FullName)" }
        throw "Release safety check failed."
    }
    foreach ($required in "Moonrise.exe", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md", "runtime\bridge\Moonrise.Native.dll") {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $required))) {
            throw "Required release entry is missing: $required"
        }
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
