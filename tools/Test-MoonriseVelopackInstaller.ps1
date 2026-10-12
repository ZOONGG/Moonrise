param([Parameter(Mandatory)][string]$SetupPath)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$root = Join-Path $repo ('artifacts/installer-smoke-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $root 'install'
$data = Join-Path $root 'data'
$uninstallKey = 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Moonrise.App'
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Moonrise.lnk'),
    (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/Moonrise.lnk')
)
if ((Test-Path $uninstallKey) -or @($shortcuts | Where-Object { Test-Path -LiteralPath $_ }).Count) {
    throw 'Existing production registration/shortcut found. Use a disposable Windows account or VM.'
}
New-Item -ItemType Directory -Path $root | Out-Null
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$process = Start-Process $setup -ArgumentList @('--silent', '--installto', ('"' + $install + '"'), '--log', ('"' + (Join-Path $root 'setup.log') + '"')) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Velopack installation failed or timed out.' }
foreach ($required in 'Update.exe','current/Moonrise.exe','current/runtime/catalog/Veyra-0.1.1-moonrise.1.jar','current/runtime/maintained-builds/moonrise-cosmetics-1.3.2.jar') {
    if (-not (Test-Path -LiteralPath (Join-Path $install $required))) { throw "Missing installed file: $required" }
}
$exe = Join-Path $install 'current/Moonrise.exe'
$version = (Get-Item $exe).VersionInfo.ProductVersion
$screenshot = Join-Path $root 'installed-launch.png'
$application = Start-Process $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru -Environment @{
    MOONRISE_VISUAL_QA = '1'; MOONRISE_VISUAL_QA_DATA_ROOT = $data
    MOONRISE_VISUAL_QA_SCREENSHOT = $screenshot; MOONRISE_VISUAL_QA_EXIT_AFTER_SCREENSHOT = '1'
}
if (-not $application.WaitForExit(30000) -or $application.ExitCode -ne 0 -or -not (Test-Path $screenshot)) {
    throw 'Installed QA launch/normal exit failed. Test installation is preserved for diagnosis.'
}
New-Item -ItemType Directory -Path $data -Force | Out-Null
$sentinel = Join-Path $data 'preservation.txt'
Set-Content $sentinel 'preserve release smoke data'
$hash = (Get-FileHash $sentinel).Hash
$uninstall = Start-Process (Join-Path $install 'Update.exe') -ArgumentList @('uninstall','--silent') -WindowStyle Hidden -PassThru
if (-not $uninstall.WaitForExit(60000) -or $uninstall.ExitCode -ne 0) { throw 'Uninstall failed.' }
if ((Get-FileHash $sentinel).Hash -ne $hash) { throw 'Uninstall changed data outside the installation.' }
[pscustomobject]@{ Status='PASS'; Version=$version; Install=$install; Evidence=$root; Launch='QA isolated data, graceful exit'; Uninstall='PASS'; DataPreserved='PASS'; Tray='Not exercised in QA mode' } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $root 'result.json')
