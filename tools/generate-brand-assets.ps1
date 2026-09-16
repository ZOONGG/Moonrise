param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputDirectory = Join-Path $repositoryRoot "assets\branding"
$project = Join-Path $PSScriptRoot "IconGenerator\IconGenerator.csproj"

dotnet run --project $project --configuration $Configuration -- $outputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Moonrise brand asset generation failed with exit code $LASTEXITCODE."
}

Write-Output "Moonrise brand assets are up to date."
