param(
    [string]$OutputPath = "user-agents\Moonrise-Cosmetics.jar",
    [switch]$SkipProbe
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$extensionRoot = $PSScriptRoot
$buildRoot = Join-Path $repositoryRoot "runtime\extension-build\moonrise-cosmetics"
$classes = Join-Path $buildRoot "classes"
$probeClasses = Join-Path $buildRoot "probe-classes"
$weaveJar = Join-Path $buildRoot "weave-loader-1.3.4.jar"
$officialWeaveUrl = "https://github.com/Weave-MC/Weave-Loader/releases/download/1.3.4/Weave-Loader-Agent-1.3.4.jar"
$expectedWeaveSha256 = "013b7b9a4ca01a473a1ffc0b031c843c70fe61616e9048fb326e03794c8457b7"

$javaHomeCandidates = @(
    "C:\Program Files\Java\jdk-25.0.3\bin",
    "C:\Program Files\Java\jdk-26.0.1\bin"
)
$javaBin = $javaHomeCandidates | Where-Object { Test-Path (Join-Path $_ "javac.exe") } | Select-Object -First 1
if (-not $javaBin) { throw "A JDK with javac and jar is required." }

New-Item -ItemType Directory -Path $classes,$probeClasses,(Split-Path $weaveJar) -Force | Out-Null
if (-not (Test-Path $weaveJar) -or
    (Get-FileHash -Algorithm SHA256 $weaveJar).Hash.ToLowerInvariant() -ne $expectedWeaveSha256) {
    Invoke-WebRequest -Uri $officialWeaveUrl -OutFile $weaveJar
}
$actualWeaveSha256 = (Get-FileHash -Algorithm SHA256 $weaveJar).Hash.ToLowerInvariant()
if ($actualWeaveSha256 -ne $expectedWeaveSha256) {
    throw "Official Weave Loader SHA-256 mismatch: $actualWeaveSha256"
}

if (Test-Path -LiteralPath $classes) {
    Remove-Item -LiteralPath $classes -Recurse -Force
}
if (Test-Path -LiteralPath $probeClasses) {
    Remove-Item -LiteralPath $probeClasses -Recurse -Force
}
New-Item -ItemType Directory -Path $classes,$probeClasses -Force | Out-Null

$sources = Get-ChildItem (Join-Path $extensionRoot "src") -Recurse -Filter "*.java" | ForEach-Object FullName
& (Join-Path $javaBin "javac.exe") --release 8 -Xlint:-options -cp $weaveJar -d $classes $sources
if ($LASTEXITCODE -ne 0) { throw "Extension compilation failed with code $LASTEXITCODE." }

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repositoryRoot $OutputPath }
New-Item -ItemType Directory -Path (Split-Path $resolvedOutput) -Force | Out-Null
& (Join-Path $javaBin "jar.exe") cfm $resolvedOutput (Join-Path $extensionRoot "MANIFEST.MF") -C $classes .
if ($LASTEXITCODE -ne 0) { throw "Extension packaging failed with code $LASTEXITCODE." }

if (-not $SkipProbe) {
    $lunarJar = Join-Path $env:USERPROFILE ".lunarclient\offline\multiver\lunar.jar"
    if (Test-Path $lunarJar) {
        $probeSources = Get-ChildItem (Join-Path $extensionRoot "tests") -Recurse -Filter "*.java" | ForEach-Object FullName
        $compileClasspath = "$weaveJar;$resolvedOutput"
        & (Join-Path $javaBin "javac.exe") --release 8 -Xlint:-options -cp $compileClasspath -d $probeClasses $probeSources
        if ($LASTEXITCODE -ne 0) { throw "Probe compilation failed with code $LASTEXITCODE." }
        $probeClasspath = "$probeClasses;$compileClasspath"
        & (Join-Path $javaBin "java.exe") -cp $probeClasspath moonrise.cosmetics.CosmeticsTransformerProbe $lunarJar $resolvedOutput
        if ($LASTEXITCODE -ne 0) { throw "Probe failed with code $LASTEXITCODE." }
        $stateProbeDirectory = Join-Path $buildRoot "state-probe"
        if (Test-Path -LiteralPath $stateProbeDirectory) {
            Remove-Item -LiteralPath $stateProbeDirectory -Recurse -Force
        }
        & (Join-Path $javaBin "java.exe") -cp $probeClasspath moonrise.cosmetics.CosmeticsPersistenceRuntimeProbe $lunarJar $resolvedOutput $stateProbeDirectory
        if ($LASTEXITCODE -ne 0) { throw "Persistence runtime probe failed with code $LASTEXITCODE." }
        & (Join-Path $javaBin "java.exe") -cp $probeClasspath moonrise.cosmetics.CosmeticsServiceRouteProbe
        if ($LASTEXITCODE -ne 0) { throw "Service route probe failed with code $LASTEXITCODE." }
    }
}

Get-FileHash -Algorithm SHA256 $resolvedOutput
