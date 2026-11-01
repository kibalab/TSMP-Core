param(
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Results,
    [switch]$Vm,
    [string]$UnityEditor='C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe',
    [ValidateRange(60,3600)][int]$TimeoutSeconds=1200
)
$ErrorActionPreference='Stop'
$projectPath=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'))){throw 'Use an isolated validation project.'}
$sdk=Test-Path -LiteralPath (Join-Path $projectPath 'Packages/com.vrchat.worlds/package.json')
if($sdk -ne [bool]$Vm){throw 'Use -Vm only for an SDK project; Native requires its own SDK-free project.'}
$active=@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Replace('/','\').Contains($projectPath)})
if($active.Count){throw 'The validation project is already open.'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count){throw 'Preserve evidence; use fresh results.'}
New-Item -ItemType Directory -Path $Results -Force | Out-Null
$resultsPath=(Resolve-Path -LiteralPath $Results).Path
$fixture=Join-Path $projectPath 'Assets/Validation/Float32Contract'
foreach($part in @('Editor','Runtime')){
    New-Item -ItemType Directory -Path (Join-Path $fixture $part),(Join-Path $resultsPath $part) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "$part/Float32Contract$(if($part -eq 'Editor'){'Validation'}else{'Probe'}).cs") -Destination (Join-Path $fixture $part) -Force
    Copy-Item -LiteralPath (Join-Path $fixture "$part/Float32Contract$(if($part -eq 'Editor'){'Validation'}else{'Probe'}).cs") -Destination (Join-Path $resultsPath $part)
}
function Get-ContractConfigurations {
    $files=@((Join-Path $projectPath 'Packages/manifest.json'),(Join-Path $projectPath 'Packages/packages-lock.json'))
    $files+=@(Get-ChildItem -LiteralPath (Join-Path $projectPath 'ProjectSettings') -File | ForEach-Object FullName)
    @($files | Where-Object {Test-Path -LiteralPath $_} | ForEach-Object {[PSCustomObject]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
}
$before=Get-ContractConfigurations
ConvertTo-Json -InputObject @($before) -Depth 4 | Set-Content -LiteralPath (Join-Path $resultsPath 'configuration-before.json') -Encoding utf8
$savedResult=$env:TSMP_VALIDATION_RESULT
try {
    $env:TSMP_VALIDATION_RESULT=Join-Path $resultsPath 'result.txt'
    $method=if($Vm){'RunVm'}else{'RunNative'}
    $log=Join-Path $resultsPath 'editor.log'
    $arguments="-batchmode -force-d3d11 -projectPath `"$projectPath`" -executeMethod Float32ContractValidation.$method -logFile `"$log`""
    $process=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($process.Id) Log=$log"
    if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw "Unity timed out; inspect PID $($process.Id): $log"}
    $process.Refresh()
    $result=Join-Path $resultsPath 'result.txt'
    if(Test-Path -LiteralPath $result){Get-Content -LiteralPath $result}
    if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS'){throw "Float32 contract failed: $log"}
} finally {
    $env:TSMP_VALIDATION_RESULT=$savedResult
    $after=Get-ContractConfigurations
    ConvertTo-Json -InputObject @($after) -Depth 4 | Set-Content -LiteralPath (Join-Path $resultsPath 'configuration-after.json') -Encoding utf8
    # A fresh project's first import may create files, but existing configuration must stay exact.
    foreach($file in $before){
        $current=@($after | Where-Object path -eq $file.path)
        if($current.Count -ne 1 -or $current[0].sha256 -ne $file.sha256){throw ('Existing configuration changed: '+$file.path)}
    }
}
