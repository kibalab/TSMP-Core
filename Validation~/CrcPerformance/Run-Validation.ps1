param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Results,
    [ValidateRange(0, 100)][int]$Warmup = 20,
    [ValidateRange(20, 200)][int]$Samples = 60,
    [ValidateRange(30, 3600)][int]$TimeoutSeconds = 900
)
$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'))) { throw 'Use a dedicated SDK Unity project.' }
$manifest = Get-Content -LiteralPath (Join-Path $projectPath 'Packages/manifest.json') -Raw | ConvertFrom-Json
$sdk = (Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds/package.json')) -or ($manifest.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds')
if (-not $sdk) { throw 'CRC VM benchmark requires the Worlds SDK project.' }
if ((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0) { throw 'Choose an empty results directory to preserve prior evidence.' }
$scripts = Join-Path $projectPath 'Assets/CrcPerformance'
New-Item -ItemType Directory -Path $scripts,$Results -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $scripts -Recurse -Force
$resultsPath = (Resolve-Path -LiteralPath $Results).Path
$oldResults = $env:TSMP_CRC_RESULTS
$oldWarmup = $env:TSMP_CRC_WARMUP
$oldSamples = $env:TSMP_CRC_SAMPLES
$oldRevision = $env:TSMP_CRC_REVISION
try {
    $env:TSMP_CRC_RESULTS = $resultsPath
    $env:TSMP_CRC_WARMUP = [string]$Warmup
    $env:TSMP_CRC_SAMPLES = [string]$Samples
    $env:TSMP_CRC_REVISION = git -C (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) rev-parse HEAD
    $log = Join-Path $resultsPath 'editor.log'
    $arguments = "-batchmode -nographics -projectPath `"$projectPath`" -executeMethod CrcPerformanceValidation.Run -logFile `"$log`""
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($process.Id) Log=$log Results=$resultsPath"
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); throw "CRC benchmark timed out: $log" }
    $process.Refresh()
    $result = Join-Path $resultsPath 'result.txt'
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
    $compilerErrors = @(Select-String -LiteralPath $log -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed|all compiler errors have to be fixed)' | ForEach-Object Line)
    if ($compilerErrors.Count -gt 0) { @('FAIL','CompilerFailure') + $compilerErrors | Set-Content -LiteralPath $result -Encoding utf8; throw "CRC benchmark compilation failed: $log" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS') { throw "CRC benchmark did not pass: $log" }
    Get-Content -LiteralPath (Join-Path $resultsPath 'summary.csv')
} finally {
    $env:TSMP_CRC_RESULTS = $oldResults
    $env:TSMP_CRC_WARMUP = $oldWarmup
    $env:TSMP_CRC_SAMPLES = $oldSamples
    $env:TSMP_CRC_REVISION = $oldRevision
}
