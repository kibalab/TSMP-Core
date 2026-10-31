param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Results,
    [switch]$Vm,
    [ValidateRange(30,3600)][int]$TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$recoveryProject=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $recoveryProject 'ProjectSettings/ProjectVersion.txt'))){throw 'Use a dedicated validation project'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0){throw 'Use fresh results'}
$recoveryAssets=Join-Path $recoveryProject 'Assets/Validation/BudgetRecovery'
New-Item -ItemType Directory -Path $recoveryAssets,$Results -Force|Out-Null
$recoveryResults=(Resolve-Path -LiteralPath $Results).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $recoveryAssets -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $recoveryResults -Recurse
$recoveryPaths=@('Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.cs','Packages/com.kibalab.tsmp.core/Runtime/Encoder/Helpers/EncoderNativeFrameBuilder.cs','Packages/com.kibalab.tsmp.core/Runtime/Encoder/Helpers/EncoderNativeSendState.cs','Packages/com.kibalab.tsmp.core/Editor/TSMPEncoderEditor.cs')
$recoveryPaths|ForEach-Object{Get-FileHash -LiteralPath (Join-Path $recoveryProject $_)}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $recoveryResults 'product-before.json') -Encoding utf8
$recoveryConfig=@('Packages/manifest.json','Packages/packages-lock.json','ProjectSettings/ProjectSettings.asset')
$recoveryConfig|ForEach-Object{Get-FileHash -LiteralPath (Join-Path $recoveryProject $_)}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $recoveryResults 'config-before.json') -Encoding utf8
$recoveryPrevious=$env:TSMP_BUDGET_RECOVERY_RESULTS
try{
    $env:TSMP_BUDGET_RECOVERY_RESULTS=$recoveryResults
    $recoveryMethod=if($Vm){'BudgetRecoveryValidation.RunVm'}else{'BudgetRecoveryValidation.Run'}
    $recoveryLog=Join-Path $recoveryResults 'editor.log'
    $recoveryArguments="-batchmode -force-d3d11 -projectPath `"$recoveryProject`" -executeMethod $recoveryMethod -logFile `"$recoveryLog`""
    $recoveryProcess=Start-Process -FilePath $UnityEditor -ArgumentList $recoveryArguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($recoveryProcess.Id) Log=$recoveryLog"
    if(-not$recoveryProcess.WaitForExit($TimeoutSeconds*1000)){$recoveryProcess.Kill();throw "Budget recovery timed out: $recoveryLog"}
    $recoveryProcess.Refresh()
    $recoveryResult=Join-Path $recoveryResults 'result.txt'
    if(Test-Path -LiteralPath $recoveryResult){Get-Content -LiteralPath $recoveryResult}
    if($recoveryProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $recoveryResult) -or (Get-Content -LiteralPath $recoveryResult -First 1) -ne 'PASS'){throw "Budget recovery failed: $recoveryLog"}
}finally{
    $env:TSMP_BUDGET_RECOVERY_RESULTS=$recoveryPrevious
    $recoveryConfig|ForEach-Object{Get-FileHash -LiteralPath (Join-Path $recoveryProject $_)}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $recoveryResults 'config-after.json') -Encoding utf8
    $recoveryPaths|ForEach-Object{Get-FileHash -LiteralPath (Join-Path $recoveryProject $_)}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $recoveryResults 'product-after.json') -Encoding utf8
}
