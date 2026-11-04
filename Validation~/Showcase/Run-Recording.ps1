param(
    [Parameter(Mandatory=$true)][string]$TemplateProject,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [string]$UnityEditor='C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe',
    [string]$Ffmpeg='C:/Windows/ffmpeg.exe'
)
$ErrorActionPreference='Stop'
$showcaseTemplate=(Resolve-Path -LiteralPath $TemplateProject).Path
if(Test-Path -LiteralPath $WorkDirectory){throw 'Use a new work directory; preserve previous recordings and logs'}
$showcaseManifest=Get-Content -LiteralPath (Join-Path $showcaseTemplate 'Packages/manifest.json') -Raw | ConvertFrom-Json
if($showcaseManifest.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds'){throw 'Use a Native Unity template without Worlds SDK'}
$showcaseRoot=[IO.Path]::GetFullPath($WorkDirectory)
$showcaseProject=Join-Path $showcaseRoot 'project'
New-Item -ItemType Directory -Path "$showcaseProject/Assets/Showcase/Editor" -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $showcaseTemplate 'Packages'),(Join-Path $showcaseTemplate 'ProjectSettings') -Destination $showcaseProject -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime') -Destination "$showcaseProject/Assets/Showcase" -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/ShowcaseBuilder.cs') -Destination "$showcaseProject/Assets/Showcase/Editor"
$showcaseApp=Join-Path $showcaseRoot 'build/TSMPShowcase.exe'
$showcaseSavedBuild=$env:TSMP_SHOWCASE_BUILD
$showcaseSender=$null;$showcaseReceiver=$null
try {
    $env:TSMP_SHOWCASE_BUILD=$showcaseApp
    $showcaseBuildArgs='-batchmode -force-d3d11 -projectPath "'+$showcaseProject+'" -executeMethod ShowcaseBuilder.Build -logFile "'+$showcaseRoot+'/build.log"'
    $showcaseBuild=Start-Process -FilePath $UnityEditor -ArgumentList $showcaseBuildArgs -WindowStyle Hidden -PassThru
    if(-not $showcaseBuild.WaitForExit(1200000)){throw 'Build timed out; inspect the owned build process'}
    $showcaseBuild.Refresh();if($showcaseBuild.ExitCode -ne 0){throw 'Build failed; preserve build.log'}
    $showcaseReport=Get-Content -LiteralPath ([IO.Path]::ChangeExtension($showcaseApp,'.build-report.txt')) -Raw
    if($showcaseReport -notmatch 'Result=Succeeded' -or $showcaseReport -notmatch 'Errors=0'){throw 'Successful build receipt missing'}
    $showcasePortProbe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $showcasePortProbe.Start();$showcasePort=$showcasePortProbe.LocalEndpoint.Port;$showcasePortProbe.Stop()
    $showcaseSenderArgs='-batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 640 -screen-height 480 -logFile "'+$showcaseRoot+'/sender.log" --role sender --capture-root "'+$showcaseRoot+'" --duration 16 --port '+$showcasePort
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
    foreach($showcaseOwned in @($showcaseReceiver,$showcaseSender)){
        if($null -ne $showcaseOwned -and -not $showcaseOwned.HasExited){
            if(-not $showcaseOwned.WaitForExit(10000)){$showcaseOwned.Kill()}
        }
    }
}
