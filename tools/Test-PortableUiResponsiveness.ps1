param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(0, 10)][int]$WarmupSeconds = 0)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MoonriseUiProbe {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
}
'@
$exe = (Resolve-Path -LiteralPath $Executable).Path
$process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited before showing a window.' }
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'Application window was not created.' }
    $handle = $process.MainWindowHandle
    if ($WarmupSeconds -gt 0) { Start-Sleep -Seconds $WarmupSeconds }
    $samples = @()
    for ($i = 0; $i -lt 100; $i++) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $result = [UIntPtr]::Zero
        $response = [MoonriseUiProbe]::SendMessageTimeout($handle, 0, [UIntPtr]::Zero,
            [IntPtr]::Zero, 2, 500, [ref]$result)
        $watch.Stop()
        $samples += [pscustomobject]@{ Milliseconds = $watch.Elapsed.TotalMilliseconds; Responded = $response -ne [IntPtr]::Zero }
        Start-Sleep -Milliseconds 100
    }
    $summary = [pscustomobject]@{
        Samples = $samples.Count
        Timeouts = @($samples | Where-Object { -not $_.Responded }).Count
        Over100ms = @($samples | Where-Object Milliseconds -gt 100).Count
        MaxMilliseconds = ($samples.Milliseconds | Measure-Object -Maximum).Maximum
        MeanMilliseconds = ($samples.Milliseconds | Measure-Object -Average).Average
    }
    $summary | ConvertTo-Json | Set-Content -LiteralPath $OutputPath
    $summary
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
