param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Results,
    [ValidateSet('Generate','Replay')][string]$Phase='Generate',
    [string]$Plan,
    [ValidateRange(30,7200)][int]$TimeoutSeconds=1800
)
$ErrorActionPreference='Stop'
$deliveryProject=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $deliveryProject 'ProjectSettings/ProjectVersion.txt'))){throw 'Dedicated Native validation project required'}
if(Test-Path -LiteralPath (Join-Path $deliveryProject 'Packages/com.vrchat.worlds')){throw 'Dedicated SDK-free project required'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count){throw 'Use a new empty results directory'}
$deliveryAssets=Join-Path $deliveryProject 'Assets/Validation/BudgetDelivery'
New-Item -ItemType Directory -Path $deliveryAssets,$Results -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime') -Destination $deliveryAssets -Recurse -Force
$deliveryRoot=(Resolve-Path -LiteralPath $Results).Path
$deliverySaved=@{}
foreach($deliveryName in @('TSMP_BUDGET_DELIVERY_RESULTS','TSMP_BUDGET_DELIVERY_PHASE','TSMP_BUDGET_DELIVERY_PLAN')){$deliverySaved[$deliveryName]=[Environment]::GetEnvironmentVariable($deliveryName)}
try{
    $env:TSMP_BUDGET_DELIVERY_RESULTS=$deliveryRoot
    $env:TSMP_BUDGET_DELIVERY_PHASE=$Phase
    $env:TSMP_BUDGET_DELIVERY_PLAN=if($Phase -eq 'Replay'){(Resolve-Path -LiteralPath $Plan).Path}else{''}
    $deliveryLog=Join-Path $deliveryRoot 'editor.log'
    $deliveryArgs="-batchmode -force-d3d11 -projectPath `"$deliveryProject`" -executeMethod BudgetDeliveryValidation.Run -logFile `"$deliveryLog`""
    $deliveryProcess=Start-Process -FilePath $UnityEditor -ArgumentList $deliveryArgs -WindowStyle Hidden -PassThru
    Write-Output "PID=$($deliveryProcess.Id) Log=$deliveryLog"
    if(-not$deliveryProcess.WaitForExit($TimeoutSeconds*1000)){$deliveryProcess.Kill();throw "Budget delivery timed out: $deliveryLog"}
    $deliveryProcess.Refresh()
    $deliveryErrors=@(Select-String -LiteralPath $deliveryLog -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)'|ForEach-Object Line)
    if($deliveryErrors.Count){$deliveryErrors|Out-String|Write-Output;throw "Budget delivery compiler errors: $deliveryLog"}
    $deliveryResult=Join-Path $deliveryRoot 'result.txt'
    if(Test-Path -LiteralPath $deliveryResult){Get-Content -LiteralPath $deliveryResult}
    if($deliveryProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $deliveryResult) -or (Get-Content -LiteralPath $deliveryResult -First 1) -ne 'PASS'){throw "Budget delivery failed: $deliveryLog"}
}finally{foreach($deliveryName in $deliverySaved.Keys){[Environment]::SetEnvironmentVariable($deliveryName,$deliverySaved[$deliveryName])}}
