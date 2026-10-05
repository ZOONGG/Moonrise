param(
    [Parameter(Mandatory = $true)][string]$LogsDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$rows = @(Get-ChildItem -LiteralPath $LogsDirectory -Filter 'launch-*.json' -File | ForEach-Object {
    $report = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    if ($report.schemaVersion -ne 2) { return }
    [pscustomobject]@{
        launchAttemptId = $report.launchAttemptId
        launchReportFile = $_.Name
        profile = $report.selectedProfile
        loaderFamily = $report.weaveLoaderFamily
        loaderVersion = $report.weaveLoaderVersion
        loaderSha256 = $report.weaveLoaderSha256
        packages = @($report.runtimePackages)
        selectedOriginalPackages = @($report.selectedPackages)
        maintainedBuilds = @($report.maintainedCompatibilityBuilds)
        orderedAgents = @($report.launchPlanAgents)
        failureOrFinalStage = $report.launchStage
        usableWindowOutcome = $report.usableWindowOutcome
        javaExitCode = $report.javaExitCode
        cleanupResult = $report.cleanupResult
        stageTimeline = @($report.stageTimeline)
        launchTimeoutSeconds = $report.launchTimeoutSeconds
        compatibilityStatus = 'untested'
        manualSmokeResult = 'pending'
    }
})
# A window or zero exit code cannot prove that a mod or agent worked in-game.
[pscustomobject]@{ schemaVersion = 1; rows = $rows } |
    ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath -Encoding utf8
