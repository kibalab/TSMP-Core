param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Results,
    [Parameter(Mandatory = $true)][ValidateSet('Native', 'Sdk')][string]$Profile,
    [string[]]$Steps,
    [switch]$IncludeSnapshot,
    [ValidateRange(30, 3600)][int]$TimeoutSeconds = 900,
    [ValidateSet('Windowed', 'Batch')][string]$PlayerMode = 'Windowed'
)

$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
$manifest = Get-Content -LiteralPath (Join-Path $Project 'Packages/manifest.json') -Raw | ConvertFrom-Json
$sdk = (Test-Path -LiteralPath (Join-Path $Project 'Packages/com.vrchat.worlds/package.json')) -or
    ($manifest.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds')
if (($Profile -eq 'Sdk') -ne $sdk) { throw 'Profile does not match the project SDK packages. Use separate dedicated Native and SDK projects.' }

[string[]]$preparation = if ($Profile -eq 'Sdk') { @('Import', 'InitializeSdk', 'PrepareSdk', 'Udon') } else { @('Import') }
$allowed = if ($Profile -eq 'Sdk') {
    @('Workflow', 'UdonArrays', 'EditorEncoding', 'NetworkEdges', 'RpcDelivery', 'RpcQueueVm', 'Configuration', 'TimelineVm', 'TransSync', 'TransSyncVm', 'ReceivePoliciesVm', 'World')
} else {
    @('Workflow', 'ArrayCache', 'EditorEncoding', 'NetworkEdges', 'RpcDelivery', 'Configuration', 'TransSync', 'TimelineRegression', 'ReceivePolicies', 'PayloadIntegrity', 'Play', 'Build', 'Player', 'Inspect')
}
if (-not $Steps) { $Steps = @($allowed | Where-Object { $_ -notin @('World', 'Inspect') }) }
foreach ($step in $Steps) {
    if ($step -notin $allowed) { throw "Step $step is not a $Profile suite step. Preparation is inserted automatically." }
}
if ('Player' -in $Steps -and 'Build' -notin $Steps) { throw 'Select Build before Player so the suite cannot use an old executable.' }
if ('Player' -in $Steps -and [array]::IndexOf($Steps, 'Player') -lt [array]::IndexOf($Steps, 'Build')) { throw 'Build must precede Player.' }
if ($IncludeSnapshot) { $Steps += 'Snapshot' } # Requires all four codec packages; see DecoderSnapshot/README.md.

$runDirectory = Join-Path $Results ($Profile + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$runDirectory = (Resolve-Path -LiteralPath $runDirectory).Path
$rows = [Collections.Generic.List[object]]::new()
$blockedBy = $null
$buildFailed = $false
foreach ($step in @($preparation + $Steps)) {
    $row = [pscustomobject]@{
        Step = $step
        Stage = if ($step -in $preparation) { 'Preparation' } elseif ($step -eq 'PayloadIntegrity') { 'Characterization' } else { 'Validation' }
        Status = 'NOTRUN'
        FailureKind = $null
        Started = (Get-Date).ToString('o')
        Seconds = 0
        Profile = $Profile
        PlayerMode = $PlayerMode
        Log = Join-Path $runDirectory ($step + '-wrapper.txt')
        Error = $null
    }
    $started = Get-Date
    if ($blockedBy -or ($step -eq 'Player' -and $buildFailed)) {
        $row.FailureKind = 'DependencyFailed'
        $row.Error = if ($blockedBy) { "Preparation failed: $blockedBy" } else { 'Build failed; an old Player was not executed.' }
    } else {
        try {
            if ($step -eq 'Snapshot') {
                $mode = if ($Profile -eq 'Sdk') { 'Udon' } else { 'Play' }
                & (Join-Path $PSScriptRoot 'DecoderSnapshot/Run-Validation.ps1') -UnityPath $UnityEditor -ProjectPath $Project -ResultsDirectory (Join-Path $runDirectory 'snapshot') -Mode $mode -TimeoutSeconds $TimeoutSeconds *> $row.Log
            } else {
                & (Join-Path $PSScriptRoot 'Run-Validation.ps1') -UnityEditor $UnityEditor -Project $Project -Results $runDirectory -Step $step -TimeoutSeconds $TimeoutSeconds -PlayerMode $PlayerMode *> $row.Log
            }
            $row.Status = 'PASS'
        } catch {
            $row.Status = 'FAIL'
            $row.FailureKind = if ($row.Stage -eq 'Preparation') { 'PreparationFailure' } else { 'ProductOrHarnessFailure' }
            $row.Error = $_.Exception.Message
            $_ | Out-String | Add-Content -LiteralPath $row.Log
            if ($row.Stage -eq 'Preparation') { $blockedBy = $step }
            if ($step -eq 'Build') { $buildFailed = $true }
        }
    }
    $row.Seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 3)
    $rows.Add($row)
    $rows | Export-Csv -LiteralPath (Join-Path $runDirectory 'checks.csv') -NoTypeInformation -Encoding utf8
    $rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDirectory 'checks.json') -Encoding utf8
    Write-Output "$($row.Status) $step ($($row.Seconds)s) $($row.Error)"
}
Write-Output "Evidence: $runDirectory"
if (@($rows | Where-Object Status -eq 'FAIL').Count -gt 0) { throw 'Suite has failures; inspect checks.csv and original logs. NOTRUN steps are not passes.' }
