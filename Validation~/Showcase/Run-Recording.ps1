param(
    [Parameter(Mandatory=$true)][string]$TemplateProject,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [string]$UnityEditor='C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe',
    [string]$Ffmpeg='C:/Windows/ffmpeg.exe',
    [string]$PrivateAssetsDirectory,
    [string]$AvatarPrefabPath,
    [string]$DanceAssetPath,
    [switch]$AoiSkirtPhysics
)
$ErrorActionPreference='Stop'
function Copy-ShowcaseDirectory([string]$Source,[string]$Destination){
    # SDK native .bundle directories require their original directory timestamps.
    # Copy only: never mirror or purge the source/destination tree.
    & robocopy $Source $Destination /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
    if($LASTEXITCODE -ge 8){throw "Directory copy failed ($LASTEXITCODE): $Source"}
}
$showcaseTemplate=(Resolve-Path -LiteralPath $TemplateProject).Path
if(Test-Path -LiteralPath $WorkDirectory){throw 'Use a new work directory; preserve previous recordings and logs'}
$showcaseManifest=Get-Content -LiteralPath (Join-Path $showcaseTemplate 'Packages/manifest.json') -Raw | ConvertFrom-Json
if($showcaseManifest.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds'){throw 'Use a Native Unity template without Worlds SDK'}
if([bool]$AvatarPrefabPath -ne [bool]$DanceAssetPath){throw 'Specify both AvatarPrefabPath and DanceAssetPath'}
if($AoiSkirtPhysics -and -not $AvatarPrefabPath){throw 'AoiSkirtPhysics requires the private Aoi prefab and dance paths'}
if($AoiSkirtPhysics -and -not (Test-Path -LiteralPath (Join-Path $showcaseTemplate 'Packages/com.vrchat.base'))){throw 'AoiSkirtPhysics requires a private embedded VRChat Base SDK copy in the template'}
if($AvatarPrefabPath -and (-not $AvatarPrefabPath.StartsWith('Assets/') -or -not $DanceAssetPath.StartsWith('Assets/'))){throw 'Private avatar and dance paths must be project-relative Assets/ paths'}
if($PrivateAssetsDirectory){$PrivateAssetsDirectory=(Resolve-Path -LiteralPath $PrivateAssetsDirectory).Path}
$showcaseRoot=[IO.Path]::GetFullPath($WorkDirectory)
$showcaseProject=Join-Path $showcaseRoot 'project'
New-Item -ItemType Directory -Path "$showcaseProject/Assets/Showcase/Editor" -Force | Out-Null
Copy-ShowcaseDirectory (Join-Path $showcaseTemplate 'Packages') "$showcaseProject/Packages"
Copy-ShowcaseDirectory (Join-Path $showcaseTemplate 'ProjectSettings') "$showcaseProject/ProjectSettings"
if($PrivateAssetsDirectory){
    # The caller provides a reviewed private Assets directory. Licensed source
    # assets stay outside the repository; SDK dependencies come from the template.
    if((Test-Path -LiteralPath (Join-Path $PrivateAssetsDirectory 'Showcase')) -or (Test-Path -LiteralPath (Join-Path $PrivateAssetsDirectory 'Showcase.meta'))){throw 'Private Assets directory must not replace the recording harness'}
    Copy-ShowcaseDirectory $PrivateAssetsDirectory "$showcaseProject/Assets"
}
Copy-ShowcaseDirectory (Join-Path $PSScriptRoot 'Runtime') "$showcaseProject/Assets/Showcase/Runtime"
Copy-ShowcaseDirectory (Join-Path $PSScriptRoot 'Editor') "$showcaseProject/Assets/Showcase/Editor"
if($AoiSkirtPhysics){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OptionalSDK/ShowcasePhysBoneBootstrap.cs') -Destination "$showcaseProject/Assets/ShowcasePhysBoneBootstrap.cs" -Force}
$showcaseApp=Join-Path $showcaseRoot 'build/TSMPShowcase.exe'
$showcaseSavedBuild=$env:TSMP_SHOWCASE_BUILD
$showcaseSavedAvatar=$env:TSMP_SHOWCASE_AVATAR
$showcaseSavedDance=$env:TSMP_SHOWCASE_DANCE
$showcaseSavedSkirt=$env:TSMP_SHOWCASE_AOI_SKIRT
$showcaseSender=$null;$showcaseReceiver=$null
try {
    $env:TSMP_SHOWCASE_BUILD=$showcaseApp
    $env:TSMP_SHOWCASE_AVATAR=$AvatarPrefabPath
    $env:TSMP_SHOWCASE_DANCE=$DanceAssetPath
    $env:TSMP_SHOWCASE_AOI_SKIRT=if($AoiSkirtPhysics){'1'}else{$null}
    $showcaseBuildArgs='-batchmode -force-d3d11 -projectPath "'+$showcaseProject+'" -executeMethod ShowcaseBuilder.Build -logFile "'+$showcaseRoot+'/build.log"'
    $showcaseBuild=Start-Process -FilePath $UnityEditor -ArgumentList $showcaseBuildArgs -WindowStyle Hidden -PassThru
    if(-not $showcaseBuild.WaitForExit(1200000)){throw 'Build timed out; inspect the owned build process'}
    $showcaseBuild.Refresh();if($showcaseBuild.ExitCode -ne 0){throw 'Build failed; preserve build.log'}
    $showcaseReport=Get-Content -LiteralPath ([IO.Path]::ChangeExtension($showcaseApp,'.build-report.txt')) -Raw
    if($showcaseReport -notmatch 'Result=Succeeded' -or $showcaseReport -notmatch 'Errors=0'){throw 'Successful build receipt missing'}
    $showcasePortProbe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $showcasePortProbe.Start();$showcasePort=$showcasePortProbe.LocalEndpoint.Port;$showcasePortProbe.Stop()
    $showcaseSenderArgs='-force-d3d11 -screen-fullscreen 0 -screen-width 640 -screen-height 480 -logFile "'+$showcaseRoot+'/sender.log" --role sender --capture-root "'+$showcaseRoot+'" --duration 16 --port '+$showcasePort
    $showcaseReceiverArgs='-force-d3d11 -screen-fullscreen 0 -screen-width 1066 -screen-height 600 -logFile "'+$showcaseRoot+'/receiver.log" --role receiver --capture-root "'+$showcaseRoot+'" --output "'+$showcaseRoot+'/showcase.mp4" --duration 16 --port '+$showcasePort+' --ffmpeg "'+$Ffmpeg+'"'
    $showcaseSender=Start-Process -FilePath $showcaseApp -ArgumentList $showcaseSenderArgs -WindowStyle Hidden -PassThru
    $showcaseReceiver=Start-Process -FilePath $showcaseApp -ArgumentList $showcaseReceiverArgs -WindowStyle Hidden -PassThru
    [ordered]@{senderPid=$showcaseSender.Id;receiverPid=$showcaseReceiver.Id;exe=$showcaseApp;port=$showcasePort;exeSha256=(Get-FileHash -LiteralPath $showcaseApp -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content -LiteralPath "$showcaseRoot/processes.json" -Encoding utf8
    if(-not $showcaseReceiver.WaitForExit(120000)){throw 'Capture timed out'}
    $showcaseReceiver.Refresh();if($showcaseReceiver.ExitCode -ne 0){throw 'Capture failed; preserve receiver log and result'}
    if((Get-Content -LiteralPath "$showcaseRoot/recording-result.txt" -First 1) -ne 'PASS'){throw 'Verified recording receipt missing'}
    Get-Content -LiteralPath "$showcaseRoot/recording-result.txt"
} finally {
    $env:TSMP_SHOWCASE_BUILD=$showcaseSavedBuild
    $env:TSMP_SHOWCASE_AVATAR=$showcaseSavedAvatar
    $env:TSMP_SHOWCASE_DANCE=$showcaseSavedDance
    $env:TSMP_SHOWCASE_AOI_SKIRT=$showcaseSavedSkirt
    foreach($showcaseOwned in @($showcaseReceiver,$showcaseSender)){
        if($null -ne $showcaseOwned -and -not $showcaseOwned.HasExited){
            if(-not $showcaseOwned.WaitForExit(10000)){$showcaseOwned.Kill()}
        }
    }
}
