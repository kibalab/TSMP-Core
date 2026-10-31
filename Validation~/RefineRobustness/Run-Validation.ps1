param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [string]$Plan,
    [Parameter(Mandatory=$true)][string]$Results,
    [ValidateRange(30,14400)][int]$TimeoutSeconds=3600,
    [switch]$CoreRecovery,
    [switch]$ModeCoverage,
    [switch]$Linear,
    [string]$SavedCases,
    [string]$SparseCases
)
$ErrorActionPreference='Stop'
if($CoreRecovery -and $ModeCoverage){throw 'Choose either CoreRecovery or ModeCoverage'}
if($Linear -and -not($ModeCoverage -or $CoreRecovery)){throw 'Linear is supported with CoreRecovery or ModeCoverage'}
$projectPath=(Resolve-Path -LiteralPath $Project).Path
$planPath=if($CoreRecovery -or $ModeCoverage){''}else{if([string]::IsNullOrWhiteSpace($Plan)){throw 'Plan is required for shadow/production measurement'};(Resolve-Path -LiteralPath $Plan).Path}
$savedCasesPath=if([string]::IsNullOrWhiteSpace($SavedCases)){''}else{(Resolve-Path -LiteralPath $SavedCases).Path}
$sparseCasesPath=if([string]::IsNullOrWhiteSpace($SparseCases)){''}else{(Resolve-Path -LiteralPath $SparseCases).Path}
if(-not(Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'))){throw 'Use a dedicated Unity validation project'}
$manifest=Get-Content -LiteralPath (Join-Path $projectPath 'Packages/manifest.json') -Raw|ConvertFrom-Json
if((Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds')) -or ($manifest.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds')){throw 'Use a dedicated SDK-free project'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0){throw 'Choose a new empty results directory'}
$assets=Join-Path $projectPath 'Assets/RefineRobustness'
New-Item -ItemType Directory -Path $assets,$Results -Force|Out-Null
$resultsPath=(Resolve-Path -LiteralPath $Results).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RefineRobustnessValidation.cs') -Destination $assets -Force
if($CoreRecovery -or $ModeCoverage){
    $validationSource=if($CoreRecovery){'CoreRecoveryValidation.cs'}else{'ModeCoverageValidation.cs'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $validationSource) -Destination $assets -Force
    $existingHelpers=@(Get-ChildItem -LiteralPath (Join-Path $projectPath 'Assets') -Filter 'DecoderSnapshotValidation.cs' -Recurse -File)
    if($existingHelpers.Count -gt 1){throw 'Multiple Snapshot helper sources would define duplicate classes'}
    $helperTarget=if($existingHelpers.Count -eq 1){$existingHelpers[0].FullName}else{Join-Path $assets 'DecoderSnapshotValidation.cs'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../DecoderSnapshot/DecoderSnapshotValidation.cs') -Destination $helperTarget -Force
}
$candidates=Join-Path $PSScriptRoot 'Candidates'
if(Test-Path -LiteralPath $candidates){
    Copy-Item -LiteralPath $candidates -Destination $assets -Recurse -Force
    Copy-Item -LiteralPath $candidates -Destination (Join-Path $resultsPath 'CandidateSources') -Recurse
}
$saved=@{}
foreach($variable in @('TSMP_ROBUSTNESS_RESULTS','TSMP_ROBUSTNESS_PLAN','TSMP_RECOVERY_CASES','TSMP_SPARSE_CASES')){$saved[$variable]=[Environment]::GetEnvironmentVariable($variable)}
try{
    $env:TSMP_ROBUSTNESS_RESULTS=$resultsPath
    $env:TSMP_ROBUSTNESS_PLAN=$planPath
    $env:TSMP_RECOVERY_CASES=$savedCasesPath
    $env:TSMP_SPARSE_CASES=$sparseCasesPath
    $log=Join-Path $resultsPath 'editor.log'
    $method=if($CoreRecovery -and $Linear){'CoreRecoveryValidation.RunLinear'}elseif($CoreRecovery){'CoreRecoveryValidation.Run'}elseif($ModeCoverage -and $Linear){'ModeCoverageValidation.RunLinear'}elseif($ModeCoverage){'ModeCoverageValidation.Run'}else{'RefineRobustnessValidation.Run'}
    $arguments="-batchmode -force-d3d11 -projectPath `"$projectPath`" -executeMethod $method -logFile `"$log`""
    $process=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($process.Id) Log=$log"
    if(-not$process.WaitForExit($TimeoutSeconds*1000)){$process.Kill();throw "Refine robustness validation timed out: $log"}
    $process.Refresh()
    $result=Join-Path $resultsPath 'result.txt'
    $compilerErrors=if(Test-Path -LiteralPath $log){@(Select-String -LiteralPath $log -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)'|ForEach-Object Line)}else{@('Unity log missing')}
    if($compilerErrors.Count -gt 0){@('FAIL','CompilerOrLaunchFailure')+$compilerErrors|Set-Content -LiteralPath $result -Encoding utf8;throw "Refine robustness compilation failed: $log"}
    if(Test-Path -LiteralPath $result){Get-Content -LiteralPath $result}
    if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS'){throw "Refine robustness validation failed: $log"}
}finally{
    foreach($variable in $saved.Keys){[Environment]::SetEnvironmentVariable($variable,$saved[$variable])}
}
