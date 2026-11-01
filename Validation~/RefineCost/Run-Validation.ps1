param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Plan,
    [Parameter(Mandatory=$true)][string]$Results,
    [string]$Revision,
    [ValidateRange(30,3600)][int]$TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$costProject=(Resolve-Path -LiteralPath $Project).Path
$costPlan=(Resolve-Path -LiteralPath $Plan).Path
if(-not(Test-Path -LiteralPath (Join-Path $costProject 'Packages/com.vrchat.worlds/package.json'))){throw 'Use a dedicated Worlds SDK project'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count){throw 'Use a fresh empty results directory'}
$costAssets=Join-Path $costProject 'Assets/Validation/RefineCost'
New-Item -ItemType Directory -Path $costAssets,$Results -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $costAssets -Recurse -Force
$costResults=(Resolve-Path -LiteralPath $Results).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $costResults -Recurse -Force
$costSavedResults=$env:TSMP_REFINE_COST_RESULTS
$costSavedPlan=$env:TSMP_REFINE_COST_PLAN
$costSavedRevision=$env:TSMP_REFINE_COST_REVISION
try{
    $env:TSMP_REFINE_COST_RESULTS=$costResults
    $env:TSMP_REFINE_COST_PLAN=$costPlan
    $env:TSMP_REFINE_COST_REVISION=$Revision
    $costLog=Join-Path $costResults 'editor.log'
    $costArguments="-batchmode -force-d3d11 -projectPath `"$costProject`" -executeMethod RefineCostValidation.Run -logFile `"$costLog`""
    $costProcess=Start-Process -FilePath $UnityEditor -ArgumentList $costArguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($costProcess.Id) Log=$costLog"
    if(-not$costProcess.WaitForExit($TimeoutSeconds*1000)){$costProcess.Kill();throw "Refine cost timed out: $costLog"}
    $costProcess.Refresh()
    $costResult=Join-Path $costResults 'result.txt'
    $costCompilerErrors=if(Test-Path -LiteralPath $costLog){@(Select-String -LiteralPath $costLog -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)'|ForEach-Object Line)}else{@('Unity log missing')}
    if($costCompilerErrors.Count){@('FAIL','CompilerOrLaunchFailure')+$costCompilerErrors|Set-Content -LiteralPath $costResult -Encoding utf8;throw "Refine cost compilation failed: $costLog"}
    if(Test-Path -LiteralPath $costResult){Get-Content -LiteralPath $costResult}
    if($costProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $costResult) -or (Get-Content -LiteralPath $costResult -First 1) -ne 'PASS'){throw "Refine cost failed: $costLog"}
}finally{
    $env:TSMP_REFINE_COST_RESULTS=$costSavedResults
    $env:TSMP_REFINE_COST_PLAN=$costSavedPlan
    $env:TSMP_REFINE_COST_REVISION=$costSavedRevision
}
