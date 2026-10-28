param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Results,
    [string]$Ffmpeg = 'ffmpeg'
)
$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'))) { throw 'Use a dedicated SDK-free Unity project.' }
if (Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds')) { throw 'VideoTransport is a Native-only validation harness.' }
$ffmpegCommand = Get-Command $Ffmpeg -ErrorAction Stop
if ((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0) {
    throw 'Choose an empty results directory; stale images must not satisfy a new roundtrip.'
}
$destination = Join-Path $projectPath 'Assets/Validation/VideoTransport'
New-Item -ItemType Directory -Path $destination,$Results -Force | Out-Null
$resultPath = (Resolve-Path -LiteralPath $Results).Path
if (Test-Path -LiteralPath (Join-Path $resultPath 'result.txt')) { throw 'Choose a new results directory; previous evidence is preserved.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor'),(Join-Path $PSScriptRoot 'Runtime') -Destination $destination -Recurse -Force
$oldResults = $env:TSMP_VIDEO_RESULTS
$oldFfmpeg = $env:TSMP_VIDEO_FFMPEG
try {
    $env:TSMP_VIDEO_RESULTS = $resultPath
    $env:TSMP_VIDEO_FFMPEG = $ffmpegCommand.Source
    & $ffmpegCommand.Source -version | Set-Content -LiteralPath (Join-Path $resultPath 'ffmpeg-version.txt') -Encoding utf8
    $log = Join-Path $resultPath 'editor.log'
    $arguments = "-batchmode -force-d3d11 -projectPath `"$projectPath`" -executeMethod VideoTransportValidation.Run -logFile `"$log`""
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(600000)) { $process.Kill(); throw "Video validation timed out: $log" }
    $process.Refresh()
    $result = Join-Path $resultPath 'result.txt'
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS') {
        throw "Video roundtrip did not pass all profiles. Retain frames.csv/result.txt and see $log."
    }
} finally {
    $env:TSMP_VIDEO_RESULTS = $oldResults
    $env:TSMP_VIDEO_FFMPEG = $oldFfmpeg
}
