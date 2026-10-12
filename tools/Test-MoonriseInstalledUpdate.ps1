param(
    [Parameter(Mandatory)][string]$SetupPath,
    [Parameter(Mandatory)][string]$FeedDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$root = Join-Path $repo ('artifacts/update-smoke-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $root 'install'
$data = Join-Path $root 'data'
$registry = 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Moonrise.App'
if ((Test-Path $registry) -or
    (Test-Path (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Moonrise.lnk')) -or
    (Test-Path (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/Moonrise.lnk'))) {
    throw 'Existing production registration/shortcut found; use a disposable Windows account.'
}
New-Item -ItemType Directory -Path $root,$data | Out-Null
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$feed = (Resolve-Path -LiteralPath $FeedDirectory).Path
function Invoke-SmokeProcess($exe, $arguments, $name, $environment = @{}) {
    $process = Start-Process $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Environment $environment `
        -RedirectStandardOutput (Join-Path $root "$name.stdout.txt") -RedirectStandardError (Join-Path $root "$name.stderr.txt")
    if (-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw "$name failed; evidence: $root" }
}
Invoke-SmokeProcess $setup @('--silent','--installto',('"'+$install+'"')) 'install'
$current = Join-Path $install 'current'
function Copy-Helper {
    Get-ChildItem (Join-Path $repo 'artifacts/update-helper') -File | Where-Object Name -ne 'Moonrise.exe' | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $current -Force
    }
}
Copy-Helper
$helper = Join-Path $current 'ReleaseUpdateSmoke.exe'
$sentinel = Join-Path $data 'test-package.jar'
Copy-Item (Join-Path $repo 'runtime/catalog/Veyra-0.1.1-moonrise.1.jar') $sentinel
Set-Content (Join-Path $data 'test-settings.json') '{"Language":"ru","Experimental":false}'
$before = @(Get-ChildItem $data -File | ForEach-Object { (Get-FileHash $_.FullName).Hash })
$badFeed = Join-Path $root 'bad-feed'
New-Item -ItemType Directory -Path $badFeed | Out-Null
Copy-Item (Join-Path $feed 'releases.beta.json') $badFeed
$metadata = Get-Content (Join-Path $badFeed 'releases.beta.json') -Raw | ConvertFrom-Json
foreach ($asset in $metadata.Assets) { [IO.File]::WriteAllBytes((Join-Path $badFeed $asset.FileName), [byte[]]@(0)) }
Invoke-SmokeProcess $helper @(('"'+$badFeed+'"'),'bad') 'corrupt-download'
$screenshot = Join-Path $root 'restarted-beta2.png'
Invoke-SmokeProcess $helper @(('"'+$feed+'"'),'apply') 'apply' @{
    MOONRISE_VISUAL_QA='1'; MOONRISE_VISUAL_QA_DATA_ROOT=$data
    MOONRISE_VISUAL_QA_SCREENSHOT=$screenshot; MOONRISE_VISUAL_QA_EXIT_AFTER_SCREENSHOT='1'
}
$deadline = [DateTime]::UtcNow.AddSeconds(45)
do { Start-Sleep -Milliseconds 250 } while (-not (Test-Path $screenshot) -and [DateTime]::UtcNow -lt $deadline)
if (-not (Test-Path $screenshot)) { throw 'No restarted application screenshot.' }
$version = (Get-Item (Join-Path $current 'Moonrise.exe')).VersionInfo.ProductVersion
if ($version -ne '0.1.0-beta.2') { throw "Unexpected installed version: $version" }
$after = @(Get-ChildItem $data -File | Where-Object Name -in 'test-package.jar','test-settings.json' | ForEach-Object { (Get-FileHash $_.FullName).Hash })
if (@(Compare-Object $before $after).Count) { throw 'Data preservation failed.' }
Start-Sleep -Seconds 2
Copy-Helper
Invoke-SmokeProcess $helper @(('"'+$feed+'"'),'none') 'no-repeat'
Invoke-SmokeProcess (Join-Path $install 'Update.exe') @('uninstall','--silent') 'uninstall'
[pscustomobject]@{ Status='PASS'; Version=$version; Evidence=$root; CorruptDownload='Rejected with no pending update'; Update='Real Windows locator, Moonrise UpdateService, native Update.exe'; Restart='QA isolated-data application screenshot'; Data='External sentinel hashes preserved'; Repeat='No newer update or pending'; Limitations='QA bypasses interactive updater UI, tray, real settings/library loading and Minecraft gates; apply rollback not tested' } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $root 'result.json')
