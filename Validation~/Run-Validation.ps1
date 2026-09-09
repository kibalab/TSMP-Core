param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Results,
    [Parameter(Mandatory = $true)][ValidateSet('Import', 'Play', 'Build', 'Player', 'Inspect', 'InitializeSdk', 'Udon', 'World', 'Workflow')][string]$Step
)

$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (!(Test-Path -LiteralPath (Join-Path $Project 'Packages/manifest.json'))) { throw 'A dedicated Unity validation project is required.' }
New-Item -ItemType Directory -Path $Results -Force | Out-Null
$Results = (Resolve-Path -LiteralPath $Results).Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $Results "$stamp-$Step.log"
$result = Join-Path $Results "$stamp-$Step-result.txt"
$build = Join-Path $Project 'Build/Mono/TSMPValidation.exe'
$oldResult = $env:TSMP_VALIDATION_RESULT
$oldBuild = $env:TSMP_VALIDATION_BUILD
try {
    $env:TSMP_VALIDATION_RESULT = $result
    $env:TSMP_VALIDATION_BUILD = $build
    if ($Step -eq 'Workflow') {
        $destination = Join-Path $Project 'Assets/Validation/Editor'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/SharedWorkflowValidation.cs') -Destination $destination -Force
    } elseif ($Step -in @('InitializeSdk', 'Udon', 'World')) {
        $destination = Join-Path $Project 'Assets/Validation/Editor'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VRCEditor/VrcSupportValidation.cs') -Destination $destination -Force
    } elseif ($Step -in @('Play', 'Build', 'Inspect')) {
        $destination = Join-Path $Project 'Assets/Validation'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime'),(Join-Path $PSScriptRoot 'Editor') -Destination $destination -Recurse -Force
    }
    $methods = @{
        Play = 'UnitySupportValidation.RunEditor'
        Build = 'UnitySupportValidation.BuildMono'
        Inspect = 'InspectorValidationWindow.Run'
        InitializeSdk = 'VrcSupportValidation.InitializeSdk'
        Udon = 'VrcSupportValidation.Compile'
        World = 'VrcSupportValidation.BuildWorld'
        Workflow = 'SharedWorkflowValidation.Run'
    }
    if ($Step -eq 'Player') {
        $arguments = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$log`""
        $process = Start-Process -FilePath $build -ArgumentList $arguments -WindowStyle Hidden -PassThru
    } else {
        $arguments = "-projectPath `"$Project`" -logFile `"$log`""
        if ($Step -ne 'Inspect') { $arguments += ' -batchmode' }
        if ($Step -in @('Import', 'Build', 'InitializeSdk', 'Udon')) { $arguments += ' -quit' }
        if ($methods.ContainsKey($Step)) { $arguments += ' -executeMethod ' + $methods[$Step] }
        if ($Step -eq 'Build') { New-Item -ItemType Directory -Path (Split-Path -Parent $build) -Force | Out-Null }
        $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    Write-Output "PID=$($process.Id) Log=$log Result=$result"
    if (!$process.WaitForExit(900000)) {
        $process.Kill()
        throw "Validation timed out; see $log"
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Validation exited $($process.ExitCode); see $log" }
    if ($Step -in @('Play', 'Player', 'Inspect', 'Udon', 'World', 'Workflow')) {
        if (!(Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS') {
            throw "Validation did not produce PASS; see $log"
        }
        Get-Content -LiteralPath $result
    }
    if ($Step -eq 'Build') { Get-Content -LiteralPath ([IO.Path]::ChangeExtension($build, '.build-report.txt')) }
} finally {
    $env:TSMP_VALIDATION_RESULT = $oldResult
    $env:TSMP_VALIDATION_BUILD = $oldBuild
}
