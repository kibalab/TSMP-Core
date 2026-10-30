param(
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Results,
    [Parameter(Mandatory=$true)][ValidateSet('baseline','optimized')][string]$Phase,
    [string]$Reference,
    [ValidateRange(60,3600)][int]$Frames=180,
    [string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe',
    [ValidateRange(60,7200)][int]$TimeoutSeconds=1800
)
$ErrorActionPreference='Stop'
$projectPath=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'))){throw 'Use the isolated Unity project'}
$packages=Get-Content -LiteralPath (Join-Path $projectPath 'Packages/manifest.json') -Raw|ConvertFrom-Json
if((Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds')) -or ($packages.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds')){throw 'Native-only harness requires the SDK-free project'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0){throw 'Choose a fresh results directory'}
if($Phase -eq 'optimized'){
    if(-not $Reference){throw 'Optimized phase requires -Reference <baseline-results>'}
    $referencePath=(Resolve-Path -LiteralPath $Reference).Path
    if((Get-Content -LiteralPath (Join-Path $referencePath 'status.txt') -First 1) -ne 'PASS'){throw 'Reference baseline did not pass'}
} else {$referencePath=''}
$destination=Join-Path $projectPath 'Assets/ColorRaster'
New-Item -ItemType Directory -Path $destination,(Join-Path $destination 'Editor'),$Results -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ColorRasterProbe.cs'),(Join-Path $PSScriptRoot 'ColorRasterValidation.cs') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/ColorRasterBuild.cs') -Destination (Join-Path $destination 'Editor') -Force
$resultsPath=(Resolve-Path -LiteralPath $Results).Path
$names=@('TSMP_COLOR_RASTER_RESULTS','TSMP_COLOR_RASTER_BUILD','TSMP_COLOR_RASTER_PHASE','TSMP_COLOR_RASTER_REFERENCE','TSMP_COLOR_RASTER_FRAMES')
$previous=@{}
foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:TSMP_COLOR_RASTER_RESULTS=$resultsPath
    $env:TSMP_COLOR_RASTER_BUILD=Join-Path $resultsPath 'Player/ColorRaster.exe'
    $env:TSMP_COLOR_RASTER_PHASE=$Phase
    $env:TSMP_COLOR_RASTER_REFERENCE=$referencePath
    $env:TSMP_COLOR_RASTER_FRAMES=[string]$Frames
    New-Item -ItemType Directory -Path (Split-Path $env:TSMP_COLOR_RASTER_BUILD) -Force|Out-Null
    $log=Join-Path $resultsPath 'editor.log'
    $arguments="-batchmode -force-d3d11 -projectPath `"$projectPath`" -executeMethod ColorRasterBuild.Build -logFile `"$log`""
    $process=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "BuildPID=$($process.Id) Phase=$Phase Log=$log"
    if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw "Build timed out; inspect owned PID $($process.Id): $log"}
    $process.Refresh()
    $errors=@(Select-String -LiteralPath $log -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)'|ForEach-Object Line)
    if($errors.Count -gt 0){$errors|Set-Content -LiteralPath (Join-Path $resultsPath 'compiler-errors.txt');throw 'Harness compilation failed; preserve evidence'}
    if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $env:TSMP_COLOR_RASTER_BUILD)){throw "Build failed: $log"}
    $log=Join-Path $resultsPath 'player.log'
    $arguments="-batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$log`""
    $process=Start-Process -FilePath $env:TSMP_COLOR_RASTER_BUILD -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "PlayerPID=$($process.Id) Log=$log"
    if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw "Player timed out; inspect owned PID $($process.Id): $log"}
    $process.Refresh()
    $status=Join-Path $resultsPath 'status.txt'
    if(Test-Path -LiteralPath $status){Get-Content -LiteralPath $status}
    if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $status) -or (Get-Content -LiteralPath $status -First 1) -ne 'PASS'){throw "Native color validation failed: $log"}
} finally {foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}}
