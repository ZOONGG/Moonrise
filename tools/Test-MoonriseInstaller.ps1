param(
    [Parameter(Mandatory)]
    [string]$SetupPath,
    [string]$DataRoot = "",
    [switch]$LaunchSmokeTest,
    [switch]$PortableLaunchIsolation,
    [switch]$TestUserDataDeletion
)

$ErrorActionPreference = "Stop"
$setup = [IO.Path]::GetFullPath($SetupPath)
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
    throw "Installer does not exist: $setup"
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "moonrise-installer-$([Guid]::NewGuid().ToString('N'))"
$installDirectory = Join-Path $testRoot "Moonrise"
$dataRoot = if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    Join-Path $env:LOCALAPPDATA "Moonrise"
} else {
    [IO.Path]::GetFullPath($DataRoot)
}
$dataExisted = Test-Path -LiteralPath $dataRoot
if ($TestUserDataDeletion -and $dataExisted) {
    throw "Refusing the user-data deletion test because $dataRoot already exists."
}

New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    function Invoke-ProcessWithTimeout {
        param(
            [Parameter(Mandatory)]
            [string]$FilePath,
            [string[]]$ArgumentList = @(),
            [Parameter(Mandatory)]
            [string]$Stage,
            [int]$TimeoutSeconds = 60
        )

        Write-Output "[installer-smoke] $Stage"
        $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -PassThru
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
            catch {
            }
            throw "$Stage timed out after $TimeoutSeconds seconds."
        }
        return $process
    }

    function Install-Moonrise {
        $process = Invoke-ProcessWithTimeout -FilePath $setup -Stage "Install Moonrise" -TimeoutSeconds 90 -ArgumentList @(
            "/VERYSILENT",
            "/CURRENTUSER",
            "/NORESTART",
            "/SUPPRESSMSGBOXES",
            "/DIR=$installDirectory",
            "/TASKS="
        )
        if ($process.ExitCode -ne 0) {
            throw "Installer exited with code $($process.ExitCode)."
        }
    }

    Install-Moonrise
    $executable = Join-Path $installDirectory "Moonrise.exe"
    $uninstaller = Join-Path $installDirectory "unins000.exe"
    foreach ($required in @($executable, $uninstaller, (Join-Path $installDirectory "runtime\bridge\Moonrise.Native.dll"))) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Clean install is missing: $required"
        }
    }

    $protocol = Get-ItemPropertyValue -LiteralPath "Registry::HKEY_CURRENT_USER\Software\Classes\moonrise\shell\open\command" -Name "(default)"
    if ($protocol -notlike "*$executable*`"%1`"*") {
        throw "moonrise:// protocol command is incorrect: $protocol"
    }

    if ($LaunchSmokeTest) {
        $portableMarker = Join-Path $installDirectory "Moonrise.portable"
        if ($PortableLaunchIsolation) {
            New-Item -ItemType File -Path $portableMarker | Out-Null
        }
        Write-Output "[installer-smoke] Launch installed Moonrise"
        $application = Start-Process -FilePath $executable -WorkingDirectory $installDirectory -PassThru
        Start-Sleep -Seconds 5
        if ($application.HasExited) {
            throw "Moonrise exited during the post-install launch smoke test with code $($application.ExitCode)."
        }
        $null = $application.CloseMainWindow()
        if (-not $application.WaitForExit(5000)) {
            Stop-Process -Id $application.Id -Force
            if (-not $application.WaitForExit(10000)) {
                throw "Moonrise did not exit after the launch smoke test."
            }
        }
        if ($PortableLaunchIsolation -and (Test-Path -LiteralPath $portableMarker)) {
            Remove-Item -LiteralPath $portableMarker -Force
        }
        if ($PortableLaunchIsolation) {
            foreach ($name in @("packages", "cache", "settings", "sessions", "logs")) {
                $portableDataPath = Join-Path $installDirectory $name
                if (Test-Path -LiteralPath $portableDataPath) {
                    Remove-Item -LiteralPath $portableDataPath -Recurse -Force
                }
            }
        }
    }

    $settingsDirectory = Join-Path $dataRoot "settings"
    $settingsDirectoryExisted = Test-Path -LiteralPath $settingsDirectory
    $sentinel = Join-Path $settingsDirectory "installer-preservation-$([Guid]::NewGuid().ToString('N')).txt"
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    Set-Content -LiteralPath $sentinel -Value "preserve"

    Install-Moonrise
    if (-not (Test-Path -LiteralPath $sentinel)) {
        throw "Upgrade removed Moonrise user data."
    }

    $uninstall = Invoke-ProcessWithTimeout -FilePath $uninstaller -Stage "Normal uninstall" -TimeoutSeconds 90 -ArgumentList @(
        "/VERYSILENT",
        "/NORESTART",
        "/SUPPRESSMSGBOXES"
    )
    if ($uninstall.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($uninstall.ExitCode)."
    }
    if (Test-Path -LiteralPath $installDirectory) {
        # Inno Setup may keep unins000.exe alive briefly while its self-delete helper
        # finishes. Wait for the directory to become empty instead of treating that
        # normal hand-off as leaked application data.
        $cleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        do {
            $remaining = @(Get-ChildItem -LiteralPath $installDirectory -Force -Recurse -ErrorAction SilentlyContinue)
            if ($remaining.Count -eq 0) {
                break
            }
            Start-Sleep -Milliseconds 250
        } while ([DateTimeOffset]::UtcNow -lt $cleanupDeadline)

        if ($remaining.Count -gt 0) {
            $remainingList = ($remaining | ForEach-Object {
                $_.FullName.Substring($installDirectory.Length).TrimStart('\')
            }) -join ", "
            throw "Uninstaller left application files in ${installDirectory}: $remainingList"
        }
    }
    if (-not (Test-Path -LiteralPath $sentinel)) {
        throw "Normal uninstall removed Moonrise user data."
    }
    if (Test-Path -LiteralPath "Registry::HKEY_CURRENT_USER\Software\Classes\moonrise") {
        throw "Uninstall did not unregister moonrise://."
    }

    if ($TestUserDataDeletion) {
        Install-Moonrise
        $uninstaller = Join-Path $installDirectory "unins000.exe"
        $uninstall = Invoke-ProcessWithTimeout -FilePath $uninstaller -Stage "Data-removal uninstall" -TimeoutSeconds 90 -ArgumentList @(
            "/VERYSILENT",
            "/NORESTART",
            "/SUPPRESSMSGBOXES",
            "/REMOVEUSERDATA"
        )
        if ($uninstall.ExitCode -ne 0) {
            throw "Data-removal uninstall exited with code $($uninstall.ExitCode)."
        }
        if (Test-Path -LiteralPath $dataRoot) {
            throw "Optional uninstall data removal did not remove $dataRoot."
        }
    }
    else {
        Remove-Item -LiteralPath $sentinel -Force
        if (-not $settingsDirectoryExisted -and
            (Get-ChildItem -LiteralPath $settingsDirectory -Force -ErrorAction SilentlyContinue).Count -eq 0) {
            Remove-Item -LiteralPath $settingsDirectory -Force
        }
        if (-not $dataExisted -and
            (Get-ChildItem -LiteralPath $dataRoot -Force -ErrorAction SilentlyContinue).Count -eq 0) {
            Remove-Item -LiteralPath $dataRoot -Force
        }
    }

    Write-Output "Installer integration tests passed."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $testRoot))
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedTestRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unsafe installer test path: $resolvedTestRoot"
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
