param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Plan,
    [Parameter(Mandatory=$true)][string]$Results,
    [switch]$Telemetry,
    [ValidateRange(60,3600)][int]$TimeoutSeconds=1200
)
$ErrorActionPreference='Stop'
$projectPath=(Resolve-Path -LiteralPath $Project).Path
$planPath=(Resolve-Path -LiteralPath $Plan).Path
if(-not(Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds/package.json'))){throw 'Use a dedicated Worlds SDK validation project.'}
$active=@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Replace('/','\').Contains($projectPath)})
if($active.Count){throw 'Dedicated project is already open.'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count){throw 'Preserve existing evidence; use fresh results.'}
New-Item -ItemType Directory -Path $Results -Force | Out-Null
$resultsPath=(Resolve-Path -LiteralPath $Results).Path
$fixture=Join-Path $projectPath 'Assets/Validation/BindingCost'
foreach($part in @('Editor','Runtime')){New-Item -ItemType Directory -Path (Join-Path $fixture $part),(Join-Path $resultsPath $part) -Force | Out-Null}
foreach($file in @('Editor/VariableLengthValidation.cs','Editor/BindingCostTelemetry.cs','Runtime/VariableLengthProbe.cs')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $fixture $file) -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $resultsPath $file)
}
function Get-VariableConfigurations {
    $files=@((Join-Path $projectPath 'Packages/manifest.json'),(Join-Path $projectPath 'Packages/packages-lock.json'))
    $files+=@(Get-ChildItem -LiteralPath (Join-Path $projectPath 'ProjectSettings') -File | ForEach-Object FullName)
    @($files | ForEach-Object {[PSCustomObject]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
}
$before=Get-VariableConfigurations
ConvertTo-Json -InputObject @($before) -Depth 4 | Set-Content -LiteralPath (Join-Path $resultsPath 'configuration-before.json') -Encoding utf8
$savedResults=$env:TSMP_VARIABLE_LENGTH_RESULTS
$savedPlan=$env:TSMP_VARIABLE_LENGTH_PLAN
$savedTelemetry=$env:TSMP_BINDING_COST_TELEMETRY
try {
    $env:TSMP_VARIABLE_LENGTH_RESULTS=$resultsPath
    $env:TSMP_VARIABLE_LENGTH_PLAN=$planPath
    $env:TSMP_BINDING_COST_TELEMETRY=if($Telemetry){'1'}else{'0'}
    $log=Join-Path $resultsPath 'editor.log'
    $argsText="-batchmode -force-d3d11 -projectPath `"$projectPath`" -executeMethod VariableLengthValidation.Run -logFile `"$log`""
    $process=Start-Process -FilePath $UnityEditor -ArgumentList $argsText -WindowStyle Hidden -PassThru
    Write-Output "PID=$($process.Id) Log=$log"
    if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw "Unity timed out; inspect PID $($process.Id): $log"}
    $process.Refresh()
    $result=Join-Path $resultsPath 'result.txt'
    $errors=@(Select-String -LiteralPath $log -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)' | ForEach-Object Line)
    if($errors.Count){
        if(Test-Path -LiteralPath $result){Copy-Item -LiteralPath $result -Destination (Join-Path $resultsPath 'fixture-result-before-controller.txt')}
        @('FAIL','CompilerOrLaunchFailure')+$errors | Set-Content -LiteralPath $result -Encoding utf8
        throw "Variable-length fixture compilation failed: $log"
    }
    if(Test-Path -LiteralPath $result){Get-Content -LiteralPath $result}
    if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS'){throw "Variable-length fixture failed: $log"}
} finally {
    $env:TSMP_VARIABLE_LENGTH_RESULTS=$savedResults
    $env:TSMP_VARIABLE_LENGTH_PLAN=$savedPlan
    $env:TSMP_BINDING_COST_TELEMETRY=$savedTelemetry
    $after=Get-VariableConfigurations
    ConvertTo-Json -InputObject @($after) -Depth 4 | Set-Content -LiteralPath (Join-Path $resultsPath 'configuration-after.json') -Encoding utf8
    if(@(Compare-Object ($before | ForEach-Object {$_.path+'='+$_.sha256}) ($after | ForEach-Object {$_.path+'='+$_.sha256})).Count){throw 'Project configuration changed.'}
}
