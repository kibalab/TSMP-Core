param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [Parameter(Mandatory = $true)][string]$Results,
    [ValidateRange(30, 7200)][int]$TimeoutSeconds = 1800
)
$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $Project).Path
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
if (-not (Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'))) { throw 'Use a dedicated Unity validation project.' }
$packages = Get-Content -LiteralPath (Join-Path $projectPath 'Packages/manifest.json') -Raw | ConvertFrom-Json
if ((Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds')) -or
    ($packages.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds')) { throw 'Transport measurement requires the dedicated SDK-free project.' }
if ((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0) { throw 'Choose an empty results directory to preserve evidence.' }
$destination = Join-Path $projectPath 'Assets/Validation/VideoTransport/Editor'
New-Item -ItemType Directory -Path $destination,$Results -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/TransportMeasurementValidation.cs') -Destination $destination -Force
$resultsPath = (Resolve-Path -LiteralPath $Results).Path
$oldManifest = $env:TSMP_TRANSPORT_MANIFEST
$oldResults = $env:TSMP_TRANSPORT_RESULTS
try {
    $env:TSMP_TRANSPORT_MANIFEST = $manifestPath
    $env:TSMP_TRANSPORT_RESULTS = $resultsPath
    $log = Join-Path $resultsPath 'editor.log'
    $arguments = "-batchmode -force-d3d11 -projectPath `"$projectPath`" -executeMethod TransportMeasurementValidation.Run -logFile `"$log`""
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($process.Id) Log=$log Results=$resultsPath"
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); throw "GPU measurement timed out: $log" }
    $process.Refresh()
    $result = Join-Path $resultsPath 'result.txt'
    $errors = @(Select-String -LiteralPath $log -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)' | ForEach-Object Line)
    if ($errors.Count -gt 0) { @('FAIL','CompilerFailure') + $errors | Set-Content -LiteralPath $result -Encoding utf8; throw "GPU measurement compilation failed: $log" }
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS') {
        throw "Some frames were rejected or mismatched, or the measurement failed. Preserve profiles.csv/frames.csv and inspect $log."
    }
} finally {
    $env:TSMP_TRANSPORT_MANIFEST = $oldManifest
    $env:TSMP_TRANSPORT_RESULTS = $oldResults
}
