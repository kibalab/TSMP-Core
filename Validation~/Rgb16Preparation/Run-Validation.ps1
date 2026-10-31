param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$InputImage,
    [Parameter(Mandatory=$true)][string]$Payload,
    [Parameter(Mandatory=$true)][string]$Results,
    [ValidateSet('fixed-first','broad-first')][string]$Order='fixed-first',
    [ValidateRange(30,3600)][int]$TimeoutSeconds=600
)
$ErrorActionPreference='Stop'
$preparationProject=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $preparationProject 'ProjectSettings/ProjectVersion.txt'))){throw 'Use a dedicated Native validation project'}
if(Test-Path -LiteralPath (Join-Path $preparationProject 'Packages/com.vrchat.worlds/package.json')){throw 'This probe requires the Native project'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count){throw 'Use a fresh results directory'}
$preparationAssets=Join-Path $preparationProject 'Assets/Validation/Rgb16Preparation'
New-Item -ItemType Directory -Path $preparationAssets,$Results -Force|Out-Null
$preparationResults=(Resolve-Path -LiteralPath $Results).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor') -Destination $preparationAssets -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor') -Destination $preparationResults -Recurse
$preparationRoots=@('Packages/com.kibalab.tsmp.core/Runtime','Packages/com.kibalab.tsmp.codec.rgb16/Runtime')
$preparationSources=foreach($preparationRoot in $preparationRoots){Get-ChildItem -LiteralPath (Join-Path $preparationProject $preparationRoot) -Recurse -File | Where-Object {$_.Extension -in @('.cs','.shader','.cginc','.mat','.prefab','.meta')} | ForEach-Object {Get-FileHash -LiteralPath $_.FullName} | Select-Object Path,Hash}
$preparationSources|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $preparationResults 'source-before.json') -Encoding utf8
$preparationConfiguration=@('Packages/manifest.json','Packages/packages-lock.json','ProjectSettings/ProjectSettings.asset')
$preparationConfiguration|ForEach-Object {Get-FileHash -LiteralPath (Join-Path $preparationProject $_)}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $preparationResults 'configuration-before.json') -Encoding utf8
$preparationNames=@('TSMP_RGB16_PREPARATION_RESULTS','TSMP_RGB16_PREPARATION_INPUT','TSMP_RGB16_PREPARATION_PAYLOAD','TSMP_RGB16_PREPARATION_ORDER')
$preparationSaved=@{}
foreach($preparationName in $preparationNames){$preparationSaved[$preparationName]=[Environment]::GetEnvironmentVariable($preparationName,'Process')}
try{
    $env:TSMP_RGB16_PREPARATION_RESULTS=$preparationResults
    $env:TSMP_RGB16_PREPARATION_INPUT=(Resolve-Path -LiteralPath $InputImage).Path
    $env:TSMP_RGB16_PREPARATION_PAYLOAD=(Resolve-Path -LiteralPath $Payload).Path
    $env:TSMP_RGB16_PREPARATION_ORDER=$Order
    $preparationLog=Join-Path $preparationResults 'editor.log'
    $preparationArguments="-batchmode -force-d3d11 -projectPath `"$preparationProject`" -executeMethod Rgb16PreparationValidation.Run -logFile `"$preparationLog`""
    $preparationProcess=Start-Process -FilePath $UnityEditor -ArgumentList $preparationArguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($preparationProcess.Id) Order=$Order Log=$preparationLog"
    if(-not$preparationProcess.WaitForExit($TimeoutSeconds*1000)){$preparationProcess.Kill();throw "RGB16 preparation timed out: $preparationLog"}
    $preparationProcess.Refresh()
    $preparationResult=Join-Path $preparationResults 'result.txt'
    $preparationErrors=@(Select-String -LiteralPath $preparationLog -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)'|ForEach-Object Line)
    if($preparationErrors.Count){@('FAIL','CompilerOrLaunchFailure')+$preparationErrors|Set-Content -LiteralPath $preparationResult -Encoding utf8;throw "RGB16 preparation compilation failed: $preparationLog"}
    if(Test-Path -LiteralPath $preparationResult){Get-Content -LiteralPath $preparationResult}
    if($preparationProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $preparationResult) -or (Get-Content -LiteralPath $preparationResult -First 1) -ne 'PASS'){throw "RGB16 preparation failed: $preparationLog"}
}finally{
    foreach($preparationName in $preparationNames){[Environment]::SetEnvironmentVariable($preparationName,$preparationSaved[$preparationName],'Process')}
    $preparationSourcesAfter=foreach($preparationRoot in $preparationRoots){Get-ChildItem -LiteralPath (Join-Path $preparationProject $preparationRoot) -Recurse -File | Where-Object {$_.Extension -in @('.cs','.shader','.cginc','.mat','.prefab','.meta')} | ForEach-Object {Get-FileHash -LiteralPath $_.FullName} | Select-Object Path,Hash}
    $preparationSourcesAfter|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $preparationResults 'source-after.json') -Encoding utf8
    $preparationConfiguration|ForEach-Object {Get-FileHash -LiteralPath (Join-Path $preparationProject $_)}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $preparationResults 'configuration-after.json') -Encoding utf8
    if(Compare-Object -ReferenceObject $preparationSources -DifferenceObject $preparationSourcesAfter -Property Path,Hash){throw 'Production sources changed during preparation measurement'}
}
