param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Results,
    [switch]$Vm,
    [ValidateRange(30,3600)][int]$TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$budgetProjectPath=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $budgetProjectPath 'ProjectSettings/ProjectVersion.txt'))){throw 'Use a dedicated Unity validation project'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0){throw 'Choose a new empty results directory'}
$budgetDestination=Join-Path $budgetProjectPath 'Assets/Validation/PayloadBudget'
New-Item -ItemType Directory -Path $budgetDestination,$Results -Force|Out-Null
$budgetResults=(Resolve-Path -LiteralPath $Results).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor'),(Join-Path $PSScriptRoot 'Runtime') -Destination $budgetDestination -Recurse -Force
$budgetSourcePaths=@(
    'Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.cs',
    'Packages/com.kibalab.tsmp.core/Runtime/Encoder/Helpers/NetworkPayloadBuffer.cs',
    'Packages/com.kibalab.tsmp.core/Runtime/Encoder/Helpers/EncoderNativeFrameBuilder.cs',
    'Packages/com.kibalab.tsmp.core/Editor/TSMPEncoderEditor.cs'
)
$budgetSourcePaths|ForEach-Object{Get-FileHash -LiteralPath (Join-Path $budgetProjectPath $_)}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $budgetResults 'runtime-source-hashes.json') -Encoding utf8
$budgetPreviousResult=$env:TSMP_VALIDATION_RESULT
try{
    $env:TSMP_VALIDATION_RESULT=Join-Path $budgetResults 'result.txt'
    $budgetLog=Join-Path $budgetResults 'editor.log'
    $budgetMethod=if($Vm){'PayloadBudgetValidation.RunVm'}else{'PayloadBudgetValidation.Run'}
    $budgetArguments="-batchmode -force-d3d11 -projectPath `"$budgetProjectPath`" -executeMethod $budgetMethod -logFile `"$budgetLog`""
    $budgetProcess=Start-Process -FilePath $UnityEditor -ArgumentList $budgetArguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($budgetProcess.Id) Log=$budgetLog"
    if(-not$budgetProcess.WaitForExit($TimeoutSeconds*1000)){$budgetProcess.Kill();throw "Payload budget validation timed out: $budgetLog"}
    $budgetProcess.Refresh()
    $budgetResult=Join-Path $budgetResults 'result.txt'
    if(Test-Path -LiteralPath $budgetResult){Get-Content -LiteralPath $budgetResult}
    if($budgetProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $budgetResult) -or (Get-Content -LiteralPath $budgetResult -First 1) -ne 'PASS'){throw "Payload budget validation failed: $budgetLog"}
}finally{$env:TSMP_VALIDATION_RESULT=$budgetPreviousResult}
