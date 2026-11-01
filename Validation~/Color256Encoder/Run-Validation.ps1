param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Results,
    [Parameter(Mandatory=$true)][string]$ExpectedSourceSha256,
    [string]$Reference,
    [ValidateRange(30,3600)][int]$TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$writerProject=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $writerProject 'Packages/com.vrchat.worlds/package.json'))){throw 'Use the dedicated SDK project; Native excludes the optimized Udon branch'}
$writerSource=Join-Path $writerProject 'Packages/com.kibalab.tsmp.codec.color256/Runtime/Scripts/TSMPCodecColor256.cs'
if((Get-FileHash -LiteralPath $writerSource -Algorithm SHA256).Hash -ne $ExpectedSourceSha256){throw 'Installed codec differs from the expected frozen source'}
if((Test-Path -LiteralPath $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count){throw 'Use a fresh output directory'}
if($Reference -and (Get-Content -LiteralPath (Join-Path $Reference 'result.txt') -First 1) -ne 'PASS'){throw 'Baseline reference must be a completed passing capture'}
$writerAssets=Join-Path $writerProject 'Assets/Validation/Color256Encoder'
New-Item -ItemType Directory -Path $writerAssets,$Results -Force|Out-Null
$writerResults=(Resolve-Path -LiteralPath $Results).Path
foreach($writerDestination in @($writerAssets,$writerResults)){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor') -Destination $writerDestination -Recurse -Force}
function Get-WriterSources {
    foreach($writerPackage in @('core','codec.luma4','codec.color256','codec.rgb16','codec.rgb20')){
        $writerPackagePath=Join-Path $writerProject ('Packages/com.kibalab.tsmp.'+$writerPackage)
        foreach($writerScope in @('Runtime','Editor','Shaders')){
            $writerScopePath=Join-Path $writerPackagePath $writerScope
            if(Test-Path -LiteralPath $writerScopePath){Get-ChildItem -LiteralPath $writerScopePath -Recurse -File | Where-Object {$_.Extension -in @('.cs','.shader','.cginc','.meta','.asmdef','.mat','.prefab')} | ForEach-Object {[PSCustomObject]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}}}
        }
    }
}
function Get-WriterConfig {
    $writerPaths=@('Packages/manifest.json','Packages/packages-lock.json')+@(Get-ChildItem -LiteralPath (Join-Path $writerProject 'ProjectSettings') -File|ForEach-Object {'ProjectSettings/'+$_.Name})
    $writerPaths|ForEach-Object {[PSCustomObject]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $writerProject $_) -Algorithm SHA256).Hash}}
}
$writerBefore=@(Get-WriterSources); $writerConfigBefore=@(Get-WriterConfig)
$writerBefore|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $writerResults 'source-before.json') -Encoding utf8
$writerConfigBefore|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $writerResults 'configuration-before.json') -Encoding utf8
$writerSavedResults=$env:TSMP_COLOR256_ENCODER_RESULTS; $writerSavedReference=$env:TSMP_COLOR256_ENCODER_REFERENCE
try{
    $env:TSMP_COLOR256_ENCODER_RESULTS=$writerResults
    $env:TSMP_COLOR256_ENCODER_REFERENCE=if($Reference){(Resolve-Path -LiteralPath $Reference).Path}else{''}
    $writerLog=Join-Path $writerResults 'editor.log'
    $writerArguments="-batchmode -force-d3d11 -projectPath `"$writerProject`" -executeMethod Color256EncoderValidation.Run -logFile `"$writerLog`""
    $writerProcess=Start-Process -FilePath $UnityEditor -ArgumentList $writerArguments -WindowStyle Hidden -PassThru
    Write-Output "PID=$($writerProcess.Id) Log=$writerLog"
    if(-not$writerProcess.WaitForExit($TimeoutSeconds*1000)){$writerProcess.Kill();throw 'Color256 writer correctness timed out'}
    $writerProcess.Refresh()
    $writerResult=Join-Path $writerResults 'result.txt'
    $writerErrors=@(Select-String -LiteralPath $writerLog -Pattern '(?i)(\berror\s+CS\d+\b|scripts have compiler errors|script compilation failed)'|ForEach-Object Line)
    if($writerErrors.Count){@('FAIL','CompilerOrLaunchFailure')+$writerErrors|Set-Content -LiteralPath $writerResult -Encoding utf8;throw 'Color256 writer harness compilation failed'}
    if(Test-Path -LiteralPath $writerResult){Get-Content -LiteralPath $writerResult}
    if($writerProcess.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $writerResult) -or (Get-Content -LiteralPath $writerResult -First 1) -ne 'PASS'){throw 'Color256 writer correctness failed'}
}finally{
    $env:TSMP_COLOR256_ENCODER_RESULTS=$writerSavedResults; $env:TSMP_COLOR256_ENCODER_REFERENCE=$writerSavedReference
    $writerAfter=@(Get-WriterSources); $writerConfigAfter=@(Get-WriterConfig)
    $writerAfter|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $writerResults 'source-after.json') -Encoding utf8
    $writerConfigAfter|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $writerResults 'configuration-after.json') -Encoding utf8
    if(Compare-Object $writerBefore $writerAfter -Property path,sha256){throw 'Installed product sources changed during correctness validation'}
    if(Compare-Object $writerConfigBefore $writerConfigAfter -Property path,sha256){throw 'Project settings changed during correctness validation'}
}
