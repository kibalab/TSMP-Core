param(
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    [Parameter(Mandatory = $true)][string]$SemanticVersioningAssembly
)

$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $SemanticVersioningAssembly).Path
$root = (Resolve-Path -LiteralPath $StagingRoot).Path
$resultPath = Join-Path $root 'manifest-results.txt'
'RUNNING' | Set-Content -LiteralPath $resultPath -Encoding utf8
try {
$inventory = Get-Content -LiteralPath (Join-Path $root 'inventory.json') -Raw | ConvertFrom-Json
$manifests = @{}
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $root 'VPM-VRC/Packages') -Directory) {
    $path = Join-Path $directory.FullName 'package.json'
    if (Test-Path -LiteralPath $path) {
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $manifests[$manifest.name] = $manifest
    }
}
$results = @('PASS', ('VPM SemanticVersioning assembly: ' + $SemanticVersioningAssembly))
$core = $inventory | Where-Object name -eq 'com.kibalab.tsmp.core'
if ($null -eq $core -or @($core).Count -ne 1) { throw 'Inventory must contain exactly one Core package.' }
$luma4 = $inventory | Where-Object name -eq 'com.kibalab.tsmp.codec.luma4'
if ($null -eq $luma4 -or @($luma4).Count -ne 1) { throw 'Inventory must contain exactly one Luma4 package for frame headers.' }
foreach ($package in $inventory) {
    $manifest = $manifests[$package.name]
    if ($manifest.version -ne $package.version) { throw "Installed version mismatch: $($package.name)" }
    foreach ($dependency in $manifest.vpmDependencies.PSObject.Properties) {
        $installed = $manifests[$dependency.Name]
        $range = [SemanticVersioning.Range]::Parse($dependency.Value)
        if ($null -eq $installed -or -not $range.IsSatisfied($installed.version, $false, $true)) {
            throw "Unsatisfied VPM dependency: $($package.name) -> $($dependency.Name) $($dependency.Value)"
        }
        $results += "$($package.name) -> $($dependency.Name) $($dependency.Value): $($installed.version) accepted with prereleases enabled"
    }
    if ($package.name -ne 'com.kibalab.tsmp.core') {
        $range = [SemanticVersioning.Range]::Parse($manifest.vpmDependencies.'com.kibalab.tsmp.core')
        foreach ($old in @('0.2.0', '0.3.0-beta.1')) {
            if ($range.IsSatisfied($old, $false, $true)) { throw "Old Core accepted: $old" }
            $results += "$($package.name): incompatible Core $old rejected"
        }
        if (-not $range.IsSatisfied($core.version, $false, $true)) { throw "Candidate Core rejected: $($core.version)" }
        if ($manifest.dependencies.'com.kibalab.tsmp.core' -ne $core.version) { throw "UPM Core dependency does not match candidate: $($package.name)" }
        $results += "$($package.name): candidate Core $($core.version) accepted"
    }
    if ($package.name -in @('com.kibalab.tsmp.codec.color256', 'com.kibalab.tsmp.codec.rgb16', 'com.kibalab.tsmp.codec.rgb20')) {
        $dependency = $manifest.vpmDependencies.'com.kibalab.tsmp.codec.luma4'
        if ([string]::IsNullOrWhiteSpace($dependency)) { throw "Missing VPM Luma4 header dependency: $($package.name)" }
        $range = [SemanticVersioning.Range]::Parse($dependency)
        if (-not $range.IsSatisfied($luma4.version, $false, $true)) { throw "Candidate Luma4 rejected: $($package.name)" }
        if ($manifest.dependencies.'com.kibalab.tsmp.codec.luma4' -ne $luma4.version) { throw "UPM Luma4 dependency does not match candidate: $($package.name)" }
        $results += "$($package.name): required header codec Luma4 $($luma4.version) declared in UPM and VPM"
    }
    if ($manifest.dependencies.PSObject.Properties.Name | Where-Object { $_.StartsWith('com.vrchat.') }) { throw "SDK dependency in UPM manifest: $($package.name)" }
}
$lock = Get-Content -LiteralPath (Join-Path $root 'UPM-NoSDK/Packages/packages-lock.json') -Raw | ConvertFrom-Json
if ($lock.dependencies.PSObject.Properties.Name | Where-Object { $_.StartsWith('com.vrchat.') }) { throw 'UPM installed a VRChat package.' }
foreach ($package in $inventory) {
    $entry = $lock.dependencies.($package.name)
    $expected = 'file:' + (Join-Path $root "packages/$($package.name)").Replace('\', '/')
    if ($entry.source -ne 'local' -or $entry.version -ne $expected) { throw "Unexpected UPM source: $($package.name)" }
    if ($package.name -ne 'com.kibalab.tsmp.core' -and $entry.dependencies.'com.kibalab.tsmp.core' -ne $core.version) {
        throw "Unexpected UPM Core dependency: $($package.name)"
    }
    if ($package.name -in @('com.kibalab.tsmp.codec.color256', 'com.kibalab.tsmp.codec.rgb16', 'com.kibalab.tsmp.codec.rgb20') -and $entry.dependencies.'com.kibalab.tsmp.codec.luma4' -ne $luma4.version) {
        throw "Unexpected UPM Luma4 dependency: $($package.name)"
    }
}
$results += 'UPM lock uses the extracted candidate packages, matching minimum Core, and no VRChat SDK.'
$results | Set-Content -LiteralPath $resultPath -Encoding utf8
$results
} catch {
    @('FAIL', $_.Exception.Message) | Set-Content -LiteralPath $resultPath -Encoding utf8
    throw
}
