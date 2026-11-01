# Plan JSON: { "fixtures": [{ "name": "color256-720p-structured",
#   "codecPrefabPath": "Packages/com.kibalab.tsmp.codec.color256/Runtime/Codec_Color256.prefab",
#   "sourceFixtureDirectory": "F:/.../fixtures/720p-structured" }] }
# Source fixture.json must describe a completed block8/sample4/30fps fixture with
# independent header/*.bin, payload/*.bin and input/*.png files. Relative source
# paths resolve against the plan file. The production raster changes only codec
# header fields/options and preserves source payload bytes. GPU checks are separate.
param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Plan,
    [Parameter(Mandatory = $true)][string]$Results,
    [ValidateRange(30, 7200)][int]$TimeoutSeconds = 1800
)
$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $Project).Path
$planPath = (Resolve-Path -LiteralPath $Plan).Path
if (-not (Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'))) { throw 'Use a dedicated Unity validation project.' }
$packages = Get-Content -LiteralPath (Join-Path $projectPath 'Packages/manifest.json') -Raw | ConvertFrom-Json
if ((Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds')) -or
    ($packages.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds')) { throw 'Codec fixture generation requires the dedicated SDK-free project.' }
if ((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0) { throw 'Choose an empty results directory to preserve evidence.' }
$destination = Join-Path $projectPath 'Assets/Validation/VideoTransport/Editor'
New-Item -ItemType Directory -Path $destination,$Results -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/CodecTransportFixtureValidation.cs') -Destination $destination -Force
$resultsPath = (Resolve-Path -LiteralPath $Results).Path
$oldPlan = $env:TSMP_CODEC_FIXTURE_PLAN
$oldResults = $env:TSMP_CODEC_FIXTURE_RESULTS
try {
    $env:TSMP_CODEC_FIXTURE_PLAN = $planPath
    $env:TSMP_CODEC_FIXTURE_RESULTS = $resultsPath
    $log = Join-Path $resultsPath 'editor.log'
    $arguments = "-batchmode -force-d3d11 -projectPath `"$projectPath`" -executeMethod CodecTransportFixtureValidation.Run -logFile `"$log`""
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($process.Id) Log=$log Results=$resultsPath"
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); throw "Codec fixture generation timed out: $log" }
    $process.Refresh()
    $result = Join-Path $resultsPath 'result.txt'
    $errors = if (Test-Path -LiteralPath $log) { @(Select-String -LiteralPath $log -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)' | ForEach-Object Line) } else { @('Unity editor log missing.') }
    if ($errors.Count -gt 0) { @('FAIL','CompilerOrLaunchFailure') + $errors | Set-Content -LiteralPath $result -Encoding utf8; throw "Codec fixture compilation/launch failed: $log" }
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS') {
        throw "Codec fixture generation failed. Preserve evidence and inspect $log."
    }
} finally {
    $env:TSMP_CODEC_FIXTURE_PLAN = $oldPlan
    $env:TSMP_CODEC_FIXTURE_RESULTS = $oldResults
}
