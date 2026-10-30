param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Evidence,
    [Parameter(Mandatory=$true)][string]$Results,
    [switch]$SkipTiming,
    [switch]$Quick,
    [string[]]$Modes=@('Color256','RGB16Fixed','RGB16Variable'),
    [string[]]$TimingRadii=@('2'),
    [ValidateRange(30,7200)][int]$TimeoutSeconds=1800
)
$ErrorActionPreference='Stop'
$selectedModes=@($Modes|ForEach-Object {$_.Split(',')|ForEach-Object {$_.Trim()}})
$selectedRadii=@($TimingRadii|ForEach-Object {$_.Split(',')|ForEach-Object {$_.Trim()}})
if($selectedModes.Count -eq 0 -or @($selectedModes|Select-Object -Unique).Count -ne $selectedModes.Count -or
    @($selectedModes|Where-Object {@('Color256','RGB16Fixed','RGB16Variable') -cnotcontains $_}).Count -gt 0){throw 'Modes must contain distinct Color256, RGB16Fixed or RGB16Variable names'}
if($selectedRadii.Count -eq 0 -or @($selectedRadii|Select-Object -Unique).Count -ne $selectedRadii.Count -or
    @($selectedRadii|Where-Object {@('1','2','3') -cnotcontains $_}).Count -gt 0){throw 'TimingRadii must contain distinct values from 1,2,3'}
$projectPath=(Resolve-Path -LiteralPath $Project).Path
$evidencePath=(Resolve-Path -LiteralPath $Evidence).Path
if(-not(Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'))){throw 'Use a dedicated Unity validation project'}
$packages=Get-Content -LiteralPath (Join-Path $projectPath 'Packages/manifest.json') -Raw|ConvertFrom-Json
if((Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds')) -or ($packages.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds')){throw 'Use the dedicated SDK-free project'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0){throw 'Choose a new empty results directory'}
$assets=Join-Path $projectPath 'Assets/RefinePerformance'
$shaders=Join-Path $assets 'Shaders'
New-Item -ItemType Directory -Path $assets,$shaders,$Results -Force|Out-Null
$resultsPath=(Resolve-Path -LiteralPath $Results).Path
$frozen=Join-Path $PSScriptRoot 'FrozenShaders'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RefinePerformanceValidation.cs') -Destination $assets -Force
Copy-Item -LiteralPath $frozen -Destination (Join-Path $resultsPath 'ShaderSources') -Recurse
foreach($file in Get-ChildItem -LiteralPath $frozen -Filter '*.shader.txt'){
    $target=Join-Path $shaders $file.Name.Substring(0,$file.Name.Length-4)
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
$saved=@{}
foreach($variable in @('TSMP_REFINE_RESULTS','TSMP_REFINE_EVIDENCE','TSMP_REFINE_SKIP_TIMING','TSMP_REFINE_QUICK','TSMP_REFINE_MODES','TSMP_REFINE_TIMING_RADII')){$saved[$variable]=[Environment]::GetEnvironmentVariable($variable)}
try{
    $env:TSMP_REFINE_RESULTS=$resultsPath
    $env:TSMP_REFINE_EVIDENCE=$evidencePath
    $env:TSMP_REFINE_SKIP_TIMING=if($SkipTiming){'1'}else{'0'}
    $env:TSMP_REFINE_QUICK=if($Quick){'1'}else{'0'}
    $env:TSMP_REFINE_MODES=$selectedModes -join ','
    $env:TSMP_REFINE_TIMING_RADII=$selectedRadii -join ','
    $log=Join-Path $resultsPath 'editor.log'
    $arguments="-batchmode -force-d3d11 -projectPath `"$projectPath`" -executeMethod RefinePerformanceValidation.Run -logFile `"$log`""
    $process=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($process.Id) Log=$log"
    if(-not$process.WaitForExit($TimeoutSeconds*1000)){$process.Kill();throw "Refine validation timed out: $log"}
    $process.Refresh()
    $result=Join-Path $resultsPath 'result.txt'
    $compilerErrors=if(Test-Path -LiteralPath $log){@(Select-String -LiteralPath $log -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)'|ForEach-Object Line)}else{@('Unity log missing')}
    if($compilerErrors.Count -gt 0){@('FAIL','CompilerOrLaunchFailure')+$compilerErrors|Set-Content -LiteralPath $result -Encoding utf8;throw "Refine validation compilation failed: $log"}
    if(Test-Path -LiteralPath $result){Get-Content -LiteralPath $result}
    if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS'){throw "Refine validation failed: $log"}
}finally{
    foreach($variable in $saved.Keys){[Environment]::SetEnvironmentVariable($variable,$saved[$variable])}
}
