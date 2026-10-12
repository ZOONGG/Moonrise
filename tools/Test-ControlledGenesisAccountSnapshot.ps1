# Offline synthetic model only. No real-store path parameter and no Lunar/JVM launch.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA ('Moonrise\temp\synthetic-account-probe-' + [guid]::NewGuid().ToString('N'))
$checks = [System.Collections.Generic.List[string]]::new()
function Check([bool]$ok, [string]$name) {
    if (-not $ok) { throw "Synthetic check failed: $name" }
    $checks.Add($name)
}
function RestrictDirectory([string]$path) {
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetOwner($sid)
    $acl.SetAccessRuleProtection($true, $false)
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $path -AclObject $acl
}
try {
    [void][System.IO.Directory]::CreateDirectory($root)
    RestrictDirectory $root
    $source = Join-Path $root 'synthetic-source.json'
    $owned = Join-Path $root 'session'
    [void][System.IO.Directory]::CreateDirectory($owned)
    $snapshot = Join-Path $owned 'accounts.json'
    $initial = '{"accounts":{"fixture":{"refreshToken":"synthetic-r0","accessToken":"synthetic-a0"}},"activeAccountLocalId":"fixture"}'
    [System.IO.File]::WriteAllText($source, $initial)
    $before = (Get-FileHash -LiteralPath $source).Hash
    # Hold a read-only, non-write-sharing handle for the copy. No source writes.
    $inputStream = [System.IO.File]::Open($source, 'Open', 'Read', 'Read')
    try {
        $outputStream = [System.IO.File]::Open($snapshot, 'CreateNew', 'Write', 'None')
        try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
        $writeDenied = $false
        try { $writer = [System.IO.File]::Open($source, 'Open', 'Write', 'ReadWrite'); $writer.Dispose() }
        catch [System.IO.IOException] { $writeDenied = $true }
        Check $writeDenied 'source write denied during copy'
    } finally { $inputStream.Dispose() }
    Check ((Get-FileHash -LiteralPath $snapshot).Hash -eq $before) 'snapshot byte identity'
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $acl = Get-Acl -LiteralPath $snapshot
    $rules = @($acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]))
    Check ($rules.Count -gt 0 -and @($rules | Where-Object { $_.IdentityReference.Value -ne $sid }).Count -eq 0) 'snapshot ACL restricted to current SID'
    $rotated = $initial.Replace('synthetic-r0', 'synthetic-r1').Replace('synthetic-a0', 'synthetic-a1')
    [System.IO.File]::WriteAllText($snapshot, $rotated)
    Check ((Get-FileHash -LiteralPath $source).Hash -eq $before) 'session writes leave source unchanged'
    # Model a provider that invalidates r0 on rotation. This is a counterexample,
    # not a claim that the installed provider always invalidates it immediately.
    $validRefresh = 'synthetic-r1'
    $originalRefresh = ([System.IO.File]::ReadAllText($source) | ConvertFrom-Json).accounts.fixture.refreshToken
    Check ($originalRefresh -ne $validRefresh) 'rotation counterexample leaves original authorization stale'
    # Model the established official RefreshAccount RPC write-before-success path.
    [System.IO.File]::WriteAllText($source, $rotated)
    Check ((Get-FileHash -LiteralPath $source).Hash -ne $before) 'official broker refresh can mutate source despite isolated game store'
    # Only fixed check names are exported. Raw runtime logs/dumps are deliberately
    # not collected: arbitrary credential values cannot be made safe by key redaction.
    $bundle = Join-Path $root 'diagnostics.json'
    [System.IO.File]::WriteAllText($bundle, (ConvertTo-Json -InputObject @($checks)))
    Check (-not [System.IO.File]::ReadAllText($bundle).Contains('synthetic-r')) 'allowlisted diagnostics omit synthetic secrets'
    # Simulated failed-session cleanup uses the same finally boundary.
    try { throw 'synthetic session failure' } catch { } finally {
        if ([System.IO.Path]::GetFullPath($owned) -ne [System.IO.Path]::Combine([System.IO.Path]::GetFullPath($root), 'session')) { throw 'Session cleanup boundary rejected' }
        Remove-Item -LiteralPath $owned -Recurse -Force
    }
    Check (-not (Test-Path -LiteralPath $owned)) 'failed-session snapshot removed'
    [void][System.IO.Directory]::CreateDirectory($owned)
    try { [System.IO.File]::WriteAllText($snapshot, $initial) } finally {
        if ([System.IO.Path]::GetFullPath($owned) -ne [System.IO.Path]::Combine([System.IO.Path]::GetFullPath($root), 'session')) { throw 'Session cleanup boundary rejected' }
        Remove-Item -LiteralPath $owned -Recurse -Force
    }
    Check (-not (Test-Path -LiteralPath $owned)) 'successful-session snapshot removed'
} finally {
    if (Test-Path -LiteralPath $root) {
        $resolved = [System.IO.Path]::GetFullPath($root)
        $parent = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Moonrise\temp')) + '\'
        if (-not $resolved.StartsWith($parent, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup boundary rejected' }
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
Check (-not (Test-Path -LiteralPath $root)) 'all synthetic material removed'
$checks | ForEach-Object { Write-Output "PASS: $_" }
Write-Output 'REAL_SMOKE_ALLOWED=NO; production and auth RPC were not exercised.'
