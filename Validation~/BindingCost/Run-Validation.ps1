param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Plan,
    [Parameter(Mandatory=$true)][string]$Results,
    [switch]$SemanticsOnly,
    [switch]$Telemetry,
    [string]$HarnessEditor,
    [ValidateRange(60,3600)][int]$TimeoutSeconds=1200
)
$ErrorActionPreference='Stop'
$bindingProject=(Resolve-Path -LiteralPath $Project).Path
$bindingPlan=(Resolve-Path -LiteralPath $Plan).Path
if(-not(Test-Path -LiteralPath (Join-Path $bindingProject 'Packages/com.vrchat.worlds/package.json'))){throw 'Use the dedicated Worlds SDK project'}
$bindingActive=@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object {$_.CommandLine -replace '/','\' -like ('*'+$bindingProject+'*')})
if($bindingActive.Count){throw 'Dedicated project is already open'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count){throw 'Use a fresh empty results directory'}
$bindingAssets=Join-Path $bindingProject 'Assets/Validation/BindingCost'
New-Item -ItemType Directory -Path $bindingAssets,$Results -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $bindingAssets -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $Results -Recurse -Force
if($HarnessEditor){
    $bindingOverride=(Resolve-Path -LiteralPath $HarnessEditor).Path
    Copy-Item -LiteralPath $bindingOverride -Destination (Join-Path $bindingAssets 'Editor/BindingCostValidation.cs') -Force
    Copy-Item -LiteralPath $bindingOverride -Destination (Join-Path $Results 'Editor/BindingCostValidation.cs') -Force
}
$bindingResults=(Resolve-Path -LiteralPath $Results).Path
function Get-BindingConfigurations {
    $bindingPaths=@('Packages/manifest.json','Packages/packages-lock.json')
    $bindingPaths+=@(Get-ChildItem -LiteralPath (Join-Path $bindingProject 'ProjectSettings') -File | ForEach-Object {'ProjectSettings/'+$_.Name})
    @($bindingPaths | ForEach-Object { $bindingConfig=Join-Path $bindingProject $_; [PSCustomObject]@{path=$_;sha256=(Get-FileHash -LiteralPath $bindingConfig -Algorithm SHA256).Hash} })
}
$bindingBefore=Get-BindingConfigurations
ConvertTo-Json -InputObject @($bindingBefore) -Depth 4 | Set-Content -LiteralPath (Join-Path $bindingResults 'configuration-before.json') -Encoding utf8
$bindingSavedResults=$env:TSMP_BINDING_COST_RESULTS
$bindingSavedPlan=$env:TSMP_BINDING_COST_PLAN
$bindingSavedSemantics=$env:TSMP_BINDING_COST_SEMANTICS_ONLY
$bindingSavedTelemetry=$env:TSMP_BINDING_COST_TELEMETRY
try {
    $env:TSMP_BINDING_COST_RESULTS=$bindingResults
    $env:TSMP_BINDING_COST_PLAN=$bindingPlan
    $env:TSMP_BINDING_COST_SEMANTICS_ONLY=if($SemanticsOnly){'1'}else{'0'}
    $env:TSMP_BINDING_COST_TELEMETRY=if($Telemetry){'1'}else{'0'}
    $bindingLog=Join-Path $bindingResults 'editor.log'
    $bindingArgs="-batchmode -force-d3d11 -projectPath `"$bindingProject`" -executeMethod BindingCostValidation.Run -logFile `"$bindingLog`""
    $bindingProcess=Start-Process -FilePath $UnityEditor -ArgumentList $bindingArgs -WindowStyle Hidden -PassThru
    Write-Output "PID=$($bindingProcess.Id) Log=$bindingLog"
    if(-not $bindingProcess.WaitForExit($TimeoutSeconds*1000)){$bindingProcess.Kill();throw "Binding cost timed out: $bindingLog"}
    $bindingProcess.Refresh()
    $bindingResult=Join-Path $bindingResults 'result.txt'
    $bindingErrors=if(Test-Path -LiteralPath $bindingLog){@(Select-String -LiteralPath $bindingLog -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)' | ForEach-Object Line)}else{@('Missing Unity log')}
    if($bindingErrors.Count){
        if(Test-Path -LiteralPath $bindingResult){Copy-Item -LiteralPath $bindingResult -Destination (Join-Path $bindingResults 'fixture-result-before-controller.txt')}
        @('FAIL','CompilerOrLaunchFailure')+$bindingErrors | Set-Content -LiteralPath $bindingResult -Encoding utf8
        throw "Binding cost compilation failed: $bindingLog"
    }
    if(Test-Path -LiteralPath $bindingResult){Get-Content -LiteralPath $bindingResult}
    if($bindingProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $bindingResult) -or (Get-Content -LiteralPath $bindingResult -First 1) -ne 'PASS'){throw "Binding cost failed: $bindingLog"}
} finally {
    $bindingAfter=Get-BindingConfigurations
    ConvertTo-Json -InputObject @($bindingAfter) -Depth 4 | Set-Content -LiteralPath (Join-Path $bindingResults 'configuration-after.json') -Encoding utf8
    $bindingDiff=@(Compare-Object ($bindingBefore | ForEach-Object {$_.path+'='+$_.sha256}) ($bindingAfter | ForEach-Object {$_.path+'='+$_.sha256}))
    ConvertTo-Json -InputObject @($bindingDiff) -Depth 4 | Set-Content -LiteralPath (Join-Path $bindingResults 'configuration-differences.json') -Encoding utf8
    $env:TSMP_BINDING_COST_RESULTS=$bindingSavedResults
    $env:TSMP_BINDING_COST_PLAN=$bindingSavedPlan
    $env:TSMP_BINDING_COST_SEMANTICS_ONLY=$bindingSavedSemantics
    $env:TSMP_BINDING_COST_TELEMETRY=$bindingSavedTelemetry
    if($bindingDiff.Count){throw 'Project configuration changed'}
}
