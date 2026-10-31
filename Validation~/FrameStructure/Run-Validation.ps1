param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Results,
    [switch]$Vm,
    [ValidateRange(30,3600)][int]$TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$structureProject=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $structureProject 'ProjectSettings/ProjectVersion.txt'))){throw 'Use a dedicated validation project'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0){throw 'Use fresh results'}
$structureAssets=Join-Path $structureProject 'Assets/Validation/FrameStructure'
New-Item -ItemType Directory -Path $structureAssets,$Results -Force|Out-Null
$structureResults=(Resolve-Path -LiteralPath $Results).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $structureAssets -Recurse -Force
$structurePaths=@('Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.cs','Packages/com.kibalab.tsmp.core/Runtime/Decoder/Helpers/NetworkFrameReader.cs')
$structurePaths|ForEach-Object{Get-FileHash -LiteralPath (Join-Path $structureProject $_)}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $structureResults 'runtime-source-hashes.json') -Encoding utf8
Get-ChildItem -LiteralPath $structureAssets -Filter '*.cs' -Recurse -File|ForEach-Object{Get-FileHash -LiteralPath $_.FullName}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $structureResults 'harness-source-hashes.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $structureResults -Recurse
$structurePrevious=$env:TSMP_STRUCTURE_RESULTS
try{
    $env:TSMP_STRUCTURE_RESULTS=$structureResults
    $structureMethod=if($Vm){'FrameStructureValidation.RunVm'}else{'FrameStructureValidation.Run'}
    $structureLog=Join-Path $structureResults 'editor.log'
    $structureArguments="-batchmode -nographics -projectPath `"$structureProject`" -executeMethod $structureMethod -logFile `"$structureLog`""
    $structureProcess=Start-Process -FilePath $UnityEditor -ArgumentList $structureArguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($structureProcess.Id) Log=$structureLog"
    if(-not$structureProcess.WaitForExit($TimeoutSeconds*1000)){$structureProcess.Kill();throw "Structure validation timed out: $structureLog"}
    $structureProcess.Refresh()
    $structureResult=Join-Path $structureResults 'result.txt'
    $structureErrors=if(Test-Path -LiteralPath $structureLog){@(Select-String -LiteralPath $structureLog -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)'|ForEach-Object Line)}else{@('Unity log missing')}
    if($structureErrors.Count){@('FAIL','CompilerOrLaunchFailure')+$structureErrors|Set-Content -LiteralPath $structureResult -Encoding utf8;throw "Structure validation compilation failed: $structureLog"}
    if(Test-Path -LiteralPath $structureResult){Get-Content -LiteralPath $structureResult}
    if($structureProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $structureResult) -or (Get-Content -LiteralPath $structureResult -First 1) -ne 'PASS'){throw "Structure validation failed: $structureLog"}
}finally{$env:TSMP_STRUCTURE_RESULTS=$structurePrevious}
