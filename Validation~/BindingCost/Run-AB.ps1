param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Manifest
)
$ErrorActionPreference='Stop'
$bindingABManifest=(Resolve-Path -LiteralPath $Manifest).Path
$bindingAB=Get-Content -LiteralPath $bindingABManifest -Raw | ConvertFrom-Json
$bindingABProject=(Resolve-Path -LiteralPath $Project).Path
$bindingABTarget=Join-Path $bindingABProject 'Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.cs'
$bindingABCore=Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.cs')
function Confirm-BindingReceipt($receipt) {
    if((Get-FileHash -LiteralPath $receipt.path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $receipt.sha256){throw ('Receipt changed: '+$receipt.path)}
}
Confirm-BindingReceipt $bindingAB.plan
Confirm-BindingReceipt $bindingAB.sources.original
Confirm-BindingReceipt $bindingAB.sources.candidate
foreach($bindingReceipt in $bindingAB.harness){Confirm-BindingReceipt $bindingReceipt}
if((Get-FileHash -LiteralPath $bindingABCore -Algorithm SHA256).Hash.ToLowerInvariant() -ne $bindingAB.sources.candidate.sha256){throw 'Authoritative Encoder must remain the pinned candidate'}
$bindingABRuns=@($bindingAB.runs)
try {
    foreach($bindingABRun in $bindingABRuns){
        $bindingABActive=@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object {$_.CommandLine -replace '/','\' -like ('*'+$bindingABProject+'*')})
        if($bindingABActive.Count){throw 'Dedicated project is already open'}
        $bindingABSource=$bindingAB.sources.($bindingABRun.variant)
        Confirm-BindingReceipt $bindingABSource
        Copy-Item -LiteralPath $bindingABSource.path -Destination $bindingABTarget -Force
        Write-Output ('Starting pair '+$bindingABRun.pair+' '+$bindingABRun.variant)
        & (Join-Path $PSScriptRoot 'Run-Validation.ps1') -UnityEditor $UnityEditor -Project $bindingABProject -Plan $bindingAB.plan.path -Results $bindingABRun.results
        & python (Join-Path $PSScriptRoot 'Analyze-Results.py') --results $bindingABRun.results
        if($LASTEXITCODE -ne 0){throw 'Raw result audit failed'}
    }
} finally {
    Copy-Item -LiteralPath $bindingAB.sources.candidate.path -Destination $bindingABTarget -Force
}
