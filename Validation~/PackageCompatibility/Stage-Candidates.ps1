param(
    [Parameter(Mandatory = $true)][string]$RepositoriesRoot,
    [Parameter(Mandatory = $true)][string]$NoSdkTemplate,
    [Parameter(Mandatory = $true)][string]$VrcTemplate,
    [Parameter(Mandatory = $true)][string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $root) { throw 'Use a new output directory to preserve earlier evidence.' }
[IO.Directory]::CreateDirectory($root) | Out-Null
$packages = Join-Path $root 'packages'
$archives = Join-Path $root 'archives'
[IO.Directory]::CreateDirectory($packages) | Out-Null
[IO.Directory]::CreateDirectory($archives) | Out-Null
$specs = @(
    @('TSMP-Core-main', 'com.kibalab.tsmp.core'),
    @('TSMPCodec-Luma4', 'com.kibalab.tsmp.codec.luma4'),
    @('TSMPCodec-RGB16', 'com.kibalab.tsmp.codec.rgb16'),
    @('TSMPCodec-RGB20', 'com.kibalab.tsmp.codec.rgb20'),
    @('TSMPCodec-Color256', 'com.kibalab.tsmp.codec.color256')
)
$coreManifest = Get-Content -LiteralPath (Join-Path $RepositoriesRoot 'TSMP-Core-main/Packages/com.kibalab.tsmp.core/package.json') -Raw | ConvertFrom-Json
$inventory = @()
foreach ($spec in $specs) {
    $repo = Join-Path $RepositoriesRoot $spec[0]
    $id = $spec[1]
    $dirty = git -C $repo status --porcelain -- "Packages/$id"
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw "Commit package changes first: $repo" }
    $commit = git -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw "Cannot read commit: $repo" }
    $candidateManifest = git -C $repo show "HEAD:Packages/$id/package.json" | Out-String | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $candidateManifest.name -ne $id) { throw "Cannot read package manifest: $id" }
    $zip = Join-Path $archives "$id-$($candidateManifest.version).zip"
    git -C $repo archive --format=zip "--output=$zip" "HEAD:Packages/$id"
    if ($LASTEXITCODE -ne 0) { throw "Cannot archive $id" }
    $destination = Join-Path $packages $id
    Expand-Archive -LiteralPath $zip -DestinationPath $destination
    $manifest = Get-Content -LiteralPath (Join-Path $destination 'package.json') -Raw | ConvertFrom-Json
    if ($manifest.name -ne $id -or $manifest.version -ne $candidateManifest.version) { throw "Version mismatch: $id" }
    if ($id -ne 'com.kibalab.tsmp.core') {
        if ($manifest.dependencies.'com.kibalab.tsmp.core' -ne $coreManifest.version) {
            throw "Core dependency mismatch: $id"
        }
    }
    if ($manifest.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds') {
        throw "SDK dependency in UPM manifest: $id"
    }
    $inventory += [ordered]@{ name = $id; version = $manifest.version; commit = $commit; archive = $zip; sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'inventory.json') -Encoding utf8

foreach ($kind in @('UPM-NoSDK', 'VPM-VRC')) {
    $template = if ($kind -eq 'UPM-NoSDK') { $NoSdkTemplate } else { $VrcTemplate }
    $project = Join-Path $root $kind
    [IO.Directory]::CreateDirectory((Join-Path $project 'Assets')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $project 'Packages')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $template 'ProjectSettings') -Destination $project -Recurse
    $manifest = Get-Content -LiteralPath (Join-Path $template 'Packages/manifest.json') -Raw | ConvertFrom-Json
    foreach ($name in @($manifest.dependencies.PSObject.Properties.Name)) {
        if ($name.StartsWith('com.kibalab.tsmp.') -or $name.StartsWith('com.vrchat.')) {
            $manifest.dependencies.PSObject.Properties.Remove($name)
        }
    }
    if ($kind -eq 'UPM-NoSDK') {
        foreach ($spec in $specs) {
            $path = (Join-Path $packages $spec[1]).Replace('\', '/')
            $manifest.dependencies | Add-Member -NotePropertyName $spec[1] -NotePropertyValue "file:$path"
        }
        $settings = Get-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectSettings.asset') -Raw
        if ($settings -match '(?m)^\s+[^\r\n]*(?:UDONSHARP|COMPILER_UDONSHARP)') { throw 'SDK-free template contains Udon symbols.' }
    } else {
        foreach ($id in @('com.vrchat.base', 'com.vrchat.worlds')) {
            $source = Join-Path $template "Packages/$id"
            if (-not (Test-Path -LiteralPath $source)) {
                $sourceManifest = Get-Content -LiteralPath (Join-Path $template 'Packages/manifest.json') -Raw | ConvertFrom-Json
                $source = $sourceManifest.dependencies.$id
                if (-not $source.StartsWith('file:')) { throw "Cannot locate SDK package $id" }
                $source = $source.Substring(5)
            }
            Copy-Item -LiteralPath $source -Destination (Join-Path $project "Packages/$id") -Recurse
        }
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Encoding utf8
}
Write-Output "Staged committed candidates in $root. VPM-VRC still needs explicit VPM installation of the staged packages."
