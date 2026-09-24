param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\artifacts\native\Moonrise.Native.dll"),
    [string]$WorkingRoot = (Join-Path $env:TEMP "moonrise-native-build")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$minHookCommit = "c3fcafdc10146beb5919319d0683e44e3c30d537"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$bridgeSource = Join-Path $repoRoot "native\Moonrise.Native\bridge.c"
if (-not (Test-Path -LiteralPath $bridgeSource)) { throw "Native bridge source was not found: $bridgeSource" }

$programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
$vswhere = Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "vswhere.exe was not found. Install Visual Studio 2022 or Build Tools with the C++ x64 workload."
}
$installationPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($installationPath)) { throw "Visual C++ x64 build tools were not found." }
$vcvars = Join-Path $installationPath "VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path -LiteralPath $vcvars)) { throw "vcvars64.bat was not found: $vcvars" }

$working = [System.IO.Path]::GetFullPath($WorkingRoot)
$minHook = Join-Path $working "minhook"
$obj = Join-Path $working "obj"
$out = [System.IO.Path]::GetFullPath($OutputPath)
$outDir = Split-Path -Parent $out
if (Test-Path -LiteralPath $working) { Remove-Item -LiteralPath $working -Recurse -Force }
New-Item -ItemType Directory -Path $working, $obj, $outDir -Force | Out-Null

Write-Output "[native] Fetching MinHook $minHookCommit"
& git clone --filter=blob:none --no-checkout https://github.com/TsudaKageyu/minhook.git $minHook
if ($LASTEXITCODE -ne 0) { throw "git clone for MinHook failed with exit code $LASTEXITCODE." }
& git -C $minHook checkout --detach $minHookCommit
if ($LASTEXITCODE -ne 0) { throw "Unable to checkout pinned MinHook commit $minHookCommit." }
$resolvedCommit = (& git -C $minHook rev-parse HEAD).Trim()
if ($resolvedCommit -ne $minHookCommit) { throw "Pinned MinHook commit mismatch: expected $minHookCommit, got $resolvedCommit." }

$sources = @(
    $bridgeSource,
    (Join-Path $minHook "src\buffer.c"),
    (Join-Path $minHook "src\hook.c"),
    (Join-Path $minHook "src\trampoline.c"),
    (Join-Path $minHook "src\hde\hde64.c")
)
foreach ($source in $sources) {
    if (-not (Test-Path -LiteralPath $source)) { throw "Native source was not found: $source" }
}

$includeRoot = Join-Path $minHook "include"
$includeSrc = Join-Path $minHook "src"
$includeHde = Join-Path $minHook "src\hde"
$pdb = Join-Path $obj "Moonrise.Native.pdb"
$headersPath = Join-Path $working "headers.txt"
$cmdFile = Join-Path $working "build-native.cmd"
$quotedSources = ($sources | ForEach-Object { '"' + $_ + '"' }) -join " "

$commands = @(
    "@echo off",
    ('call "{0}" >nul' -f $vcvars),
    ('cl /nologo /LD /O2 /GL /GS /guard:cf /DUNICODE /D_UNICODE /D_CRT_SECURE_NO_WARNINGS /W4 /WX /wd4201 /wd4244 /wd4310 /MT /I"{0}" /I"{1}" /I"{2}" {3} /link /LTCG /DYNAMICBASE /NXCOMPAT /OUT:"{4}" /PDB:"{5}"' -f $includeRoot, $includeSrc, $includeHde, $quotedSources, $out, $pdb),
    "if errorlevel 1 exit /b %errorlevel%",
    ('dumpbin /headers "{0}" > "{1}"' -f $out, $headersPath)
)
Set-Content -LiteralPath $cmdFile -Value $commands -Encoding ASCII

Write-Output "[native] Building x64 Moonrise.Native.dll"
& cmd.exe /d /c $cmdFile
if ($LASTEXITCODE -ne 0) { throw "Native bridge build failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $out)) { throw "Native bridge build did not produce $out." }

$headers = Get-Content -LiteralPath $headersPath -Raw
if ($headers -notmatch "(?im)\b8664 machine \(x64\)") { throw "Native bridge is not an x64 PE image." }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $out).Hash
$size = (Get-Item -LiteralPath $out).Length
if ($size -lt 50000) { throw "Native bridge output is unexpectedly small: $size bytes." }

Write-Output "[native] Build succeeded"
Write-Output "[native] Output: $out"
Write-Output "[native] SHA-256: $hash"
Write-Output "[native] Size: $size bytes"
