param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Plan,
    [Parameter(Mandatory=$true)][string]$Results,
    [ValidateRange(60,3600)][int]$TimeoutSeconds=1200
)
$ErrorActionPreference='Stop'
$poseProject=(Resolve-Path -LiteralPath $Project).Path
$posePlan=(Resolve-Path -LiteralPath $Plan).Path
if (-not(Test-Path -LiteralPath (Join-Path $poseProject 'Packages/com.vrchat.worlds/package.json'))) { throw 'Use a dedicated Worlds SDK validation project.' }
$poseActive=@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Replace('/','\').Contains($poseProject)})
if ($poseActive.Count) { throw 'Dedicated project is already open.' }
if ((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count) { throw 'Preserve existing evidence; use fresh results.' }
$poseAssets=Join-Path $poseProject 'Assets/Validation/PackedPose'
New-Item -ItemType Directory -Path $poseAssets,$Results -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor') -Destination $poseAssets -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor') -Destination $Results -Recurse -Force
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $Results 'Run-Validation.ps1')
$poseResults=(Resolve-Path -LiteralPath $Results).Path
function Get-PoseConfigurations {
    $posePaths=@('Packages/manifest.json','Packages/packages-lock.json')
    $posePaths+=@(Get-ChildItem -LiteralPath (Join-Path $poseProject 'ProjectSettings') -File | ForEach-Object {'ProjectSettings/'+$_.Name})
    @($posePaths | ForEach-Object { $poseConfig=Join-Path $poseProject $_; [PSCustomObject]@{path=$_;sha256=(Get-FileHash -LiteralPath $poseConfig -Algorithm SHA256).Hash} })
}
$poseBefore=Get-PoseConfigurations
ConvertTo-Json -InputObject @($poseBefore) -Depth 4 | Set-Content -LiteralPath (Join-Path $poseResults 'configuration-before.json') -Encoding utf8
$poseSavedResults=$env:TSMP_PACKED_POSE_RESULTS
$poseSavedPlan=$env:TSMP_PACKED_POSE_PLAN
try {
    $env:TSMP_PACKED_POSE_RESULTS=$poseResults
    $env:TSMP_PACKED_POSE_PLAN=$posePlan
    $poseLog=Join-Path $poseResults 'editor.log'
    $poseArgs="-batchmode -force-d3d11 -projectPath `"$poseProject`" -executeMethod PackedPoseValidation.Run -logFile `"$poseLog`""
    $poseProcess=Start-Process -FilePath $UnityEditor -ArgumentList $poseArgs -WindowStyle Hidden -PassThru
    Write-Output "PID=$($poseProcess.Id) Log=$poseLog"
    if (-not $poseProcess.WaitForExit($TimeoutSeconds*1000)) { throw "Packed pose timed out; inspect PID $($poseProcess.Id): $poseLog" }
    $poseProcess.Refresh()
    $poseResult=Join-Path $poseResults 'result.txt'
    $poseErrors=if(Test-Path -LiteralPath $poseLog){@(Select-String -LiteralPath $poseLog -Pattern '(?i)(\berror\s+(?:CS|BC)\d+\b|scripts have compiler errors|script compilation failed)' | ForEach-Object Line)}else{@('Missing Unity log')}
    if ($poseErrors.Count) {
        if(Test-Path -LiteralPath $poseResult){Copy-Item -LiteralPath $poseResult -Destination (Join-Path $poseResults 'fixture-result-before-controller.txt')}
        @('FAIL','CompilerOrLaunchFailure')+$poseErrors | Set-Content -LiteralPath $poseResult -Encoding utf8
        throw "Packed pose fixture compilation failed: $poseLog"
    }
    if(Test-Path -LiteralPath $poseResult){Get-Content -LiteralPath $poseResult}
    if($poseProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $poseResult) -or (Get-Content -LiteralPath $poseResult -First 1) -ne 'PASS'){throw "Packed pose fixture failed: $poseLog"}
} finally {
    $env:TSMP_PACKED_POSE_RESULTS=$poseSavedResults
    $env:TSMP_PACKED_POSE_PLAN=$poseSavedPlan
    $poseAfter=Get-PoseConfigurations
    ConvertTo-Json -InputObject @($poseAfter) -Depth 4 | Set-Content -LiteralPath (Join-Path $poseResults 'configuration-after.json') -Encoding utf8
    $poseDiff=@(Compare-Object ($poseBefore | ForEach-Object {$_.path+'='+$_.sha256}) ($poseAfter | ForEach-Object {$_.path+'='+$_.sha256}))
    ConvertTo-Json -InputObject @($poseDiff) -Depth 4 | Set-Content -LiteralPath (Join-Path $poseResults 'configuration-differences.json') -Encoding utf8
    if($poseDiff.Count){throw 'Project configuration changed.'}
}
