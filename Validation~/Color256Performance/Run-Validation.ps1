param(
    [Parameter(Mandatory=$true)][string]$Manifest,
    [Parameter(Mandatory=$true)][string]$Project,
    [string]$UnityEditor='C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe',
    [ValidateSet('baseline','candidate')][string]$FinalVariant='candidate'
)
$ErrorActionPreference='Stop'
$abManifestPath=(Resolve-Path -LiteralPath $Manifest).Path
$abRoot=Split-Path -Parent $abManifestPath
$abManifest=Get-Content -LiteralPath $abManifestPath -Raw | ConvertFrom-Json
$abProject=(Resolve-Path -LiteralPath $Project).Path
if(-not(Test-Path -LiteralPath (Join-Path $abProject 'Packages/com.vrchat.worlds/package.json'))){throw 'Use a dedicated Worlds SDK project.'}
$abCodec=Join-Path $abProject 'Packages/com.kibalab.tsmp.codec.color256/Runtime/Scripts/TSMPCodecColor256.cs'
$abRepo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$abUnity=(Resolve-Path -LiteralPath $UnityEditor).Path
$abInitialHash=(Get-FileHash -LiteralPath $abCodec -Algorithm SHA256).Hash.ToLowerInvariant()
if($abInitialHash -notin @($abManifest.sources.baseline.sha256.ToLowerInvariant(),$abManifest.sources.candidate.sha256.ToLowerInvariant())){throw 'Dedicated codec source is neither frozen variant; preserve it and use the matching dedicated project.'}
if(Test-Path -LiteralPath (Join-Path $abRoot 'controller-result.txt')){throw 'Preserve the existing completed/failed evidence; generate a new manifest directory.'}
$abReceipts=@()
$abRevision=(git -C $abRepo rev-parse HEAD)
function Test-ABSource([string]$Path,[string]$Expected){if((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Expected.ToLowerInvariant()){throw "Frozen source hash mismatch: $Path"}}
function Test-ABProjectIdle {
    $abProjectMatch=$abProject.Replace('/','\')
    $abOwned=@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Replace('/','\').IndexOf($abProjectMatch,[StringComparison]::OrdinalIgnoreCase) -ge 0})
    if($abOwned.Count){throw 'Dedicated VRC Unity is still running; do not replace its source.'}
}
foreach($abPlannedRun in $abManifest.runs){if((Test-Path -LiteralPath $abPlannedRun.results) -and @(Get-ChildItem -LiteralPath $abPlannedRun.results -Force).Count){throw 'Preserve existing run evidence; generate a new manifest directory.'}}
foreach($abVariant in @('baseline','candidate')){Test-ABSource $abManifest.sources.$abVariant.path $abManifest.sources.$abVariant.sha256}
try{
    foreach($abRun in $abManifest.runs){
        Test-ABProjectIdle
        Test-ABSource $abRun.plan $abRun.plan_sha256
        $abSource=$abManifest.sources.($abRun.variant)
        Copy-Item -LiteralPath $abSource.path -Destination $abCodec -Force
        Test-ABSource $abCodec $abSource.sha256
        $abReceipts+=@([PSCustomObject]@{run=$abRun.name;variant=$abRun.variant;utc=[DateTime]::UtcNow.ToString('o');source=$abSource.path;destination=$abCodec;sha256=$abSource.sha256})
        ConvertTo-Json -InputObject @($abReceipts) -Depth 5 | Set-Content -LiteralPath (Join-Path $abRoot 'source-copy-receipts.json') -Encoding utf8
        & (Join-Path $abRepo 'Validation~/SustainedCost/Run-Validation.ps1') -UnityEditor $abUnity -Project $abProject -Plan $abRun.plan -Results $abRun.results -Revision ($abRevision+';Color256='+$abSource.sha256+';variant='+$abRun.variant) -TimeoutSeconds 1200
        python (Join-Path $abRepo 'Validation~/SustainedCost/Analyze-Results.py') --results $abRun.results
        if($LASTEXITCODE -ne 0){throw 'Independent per-run analyzer failed.'}
        Test-ABSource $abCodec $abSource.sha256
        [PSCustomObject]@{completed=$abRun.name;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $abRoot 'progress.json') -Encoding utf8
    }
    'PASS7freshprocessruns' | Set-Content -LiteralPath (Join-Path $abRoot 'controller-result.txt') -Encoding utf8
}catch{
    @('FAIL',$_|Out-String) | Set-Content -LiteralPath (Join-Path $abRoot 'controller-result.txt') -Encoding utf8
    throw
}finally{
    Test-ABProjectIdle
    Test-ABSource $abManifest.sources.$FinalVariant.path $abManifest.sources.$FinalVariant.sha256
    Copy-Item -LiteralPath $abManifest.sources.$FinalVariant.path -Destination $abCodec -Force
    Test-ABSource $abCodec $abManifest.sources.$FinalVariant.sha256
    [PSCustomObject]@{utc=[DateTime]::UtcNow.ToString('o');destination=$abCodec;sha256=(Get-FileHash -LiteralPath $abCodec -Algorithm SHA256).Hash;policy=('Restore frozen '+$FinalVariant+' source after isolated source A/B.')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $abRoot 'final-project-source.json') -Encoding utf8
}
