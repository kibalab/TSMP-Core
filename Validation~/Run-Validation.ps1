param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Results,
    [Parameter(Mandatory = $true)][ValidateSet('Import', 'Play', 'Build', 'Player', 'Inspect', 'InitializeSdk', 'PrepareSdk', 'Udon', 'World', 'Workflow', 'ArrayCache', 'UdonArrays', 'EditorEncoding', 'NetworkEdges', 'RpcDelivery', 'RpcQueueVm', 'Configuration', 'TimelineVm', 'TimelineRegression', 'TransSync', 'TransSyncVm', 'ReceivePolicies', 'ReceivePoliciesVm', 'PayloadIntegrity', 'FrameStructure', 'PayloadBudget')][string]$Step,
    [ValidateRange(30, 3600)][int]$TimeoutSeconds = 900,
    [ValidateSet('Windowed', 'Batch')][string]$PlayerMode = 'Windowed'
)

$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (!(Test-Path -LiteralPath (Join-Path $Project 'Packages/manifest.json'))) { throw 'A dedicated Unity validation project is required.' }
$manifest = Get-Content -LiteralPath (Join-Path $Project 'Packages/manifest.json') -Raw | ConvertFrom-Json
$sdk = (Test-Path -LiteralPath (Join-Path $Project 'Packages/com.vrchat.worlds/package.json')) -or
    ($manifest.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds')
$nativeOnly = @('Play', 'Build', 'Player', 'Inspect', 'ArrayCache', 'TimelineRegression', 'ReceivePolicies', 'PayloadIntegrity')
$sdkOnly = @('InitializeSdk', 'PrepareSdk', 'Udon', 'World', 'UdonArrays', 'RpcQueueVm', 'TimelineVm', 'TransSyncVm', 'ReceivePoliciesVm')
if ($sdk -and $Step -in $nativeOnly) { throw "Scope mismatch: $Step is Native-only. Use the dedicated VM step in the SDK project." }
if (-not $sdk -and $Step -in $sdkOnly) { throw "Scope mismatch: $Step requires a Worlds SDK project." }
New-Item -ItemType Directory -Path $Results -Force | Out-Null
$Results = (Resolve-Path -LiteralPath $Results).Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
if ($Step -in @('FrameStructure', 'PayloadBudget')) {
    # Reuse the dedicated runners and their evidence format; no fixture copy here.
    $targetedParameters = @{
        UnityEditor = $UnityEditor
        Project = $Project
        Results = Join-Path $Results "$stamp-$Step"
        Vm = $sdk
        TimeoutSeconds = $TimeoutSeconds
    }
    & (Join-Path $PSScriptRoot "$Step/Run-Validation.ps1") @targetedParameters
    return
}
$log = Join-Path $Results "$stamp-$Step.log"
$result = Join-Path $Results "$stamp-$Step-result.txt"
$build = Join-Path $Project 'Build/Mono/TSMPValidation.exe'
$oldResult = $env:TSMP_VALIDATION_RESULT
$oldBuild = $env:TSMP_VALIDATION_BUILD

function Assert-UnityScriptCompilation([string]$LogPath, [string]$ResultPath) {
    if (-not (Test-Path -LiteralPath $LogPath)) { return }
    $diagnostics = @(Select-String -LiteralPath $LogPath -Pattern '(?i)(\berror\s+(?:CS|BC)\d+\b|scripts have compiler errors|script compilation failed|all compiler errors have to be fixed)' |
        ForEach-Object { $_.Line })
    if ($diagnostics.Count -eq 0) { return }
    @('FAIL', 'CompilerFailure', ('Log=' + $LogPath)) + $diagnostics |
        Set-Content -LiteralPath $ResultPath -Encoding utf8
    $failure = [InvalidOperationException]::new("CompilerFailure: Unity script compilation failed; see $LogPath")
    $failure.Data['FailureKind'] = 'CompilerFailure'
    $failure.Data['LogPath'] = $LogPath
    $failure.Data['ResultPath'] = $ResultPath
    $failure.Data['CompilerDiagnostics'] = $diagnostics
    throw $failure
}

try {
    $env:TSMP_VALIDATION_RESULT = $result
    $env:TSMP_VALIDATION_BUILD = $build
    if ($Step -eq 'PayloadIntegrity') {
        $destination = Join-Path $Project 'Assets/Validation/VideoTransport/Editor'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VideoTransport/Editor/PayloadIntegrityValidation.cs') -Destination $destination -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VideoTransport/Runtime') -Destination (Split-Path -Parent $destination) -Recurse -Force
    } elseif ($Step -eq 'PrepareSdk') {
        foreach ($suite in @('Configuration', 'RpcDelivery', 'TransSync', 'ReceivePolicies')) {
            $destination = Join-Path $Project "Assets/Validation/$suite"
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot "$suite/Editor"),(Join-Path $PSScriptRoot "$suite/Runtime") -Destination $destination -Recurse -Force
        }
        $arrayEditor = Join-Path $Project 'Assets/Validation/ArrayCache/Editor'
        New-Item -ItemType Directory -Path $arrayEditor -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArrayCache/Editor/ArrayCacheCases.cs'),(Join-Path $PSScriptRoot 'ArrayCache/VRCEditor/DecoderArrayCacheUdonValidation.cs') -Destination $arrayEditor -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArrayCache/Udon') -Destination (Split-Path -Parent $arrayEditor) -Recurse -Force
        $editor = Join-Path $Project 'Assets/Validation/Editor'
        New-Item -ItemType Directory -Path $editor -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VRCEditor/SdkValidationPreparation.cs') -Destination $editor -Force
    } elseif ($Step -in @('ReceivePolicies', 'ReceivePoliciesVm')) {
        $destination = Join-Path $Project 'Assets/Validation/ReceivePolicies'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ReceivePolicies/Editor'),(Join-Path $PSScriptRoot 'ReceivePolicies/Runtime') -Destination $destination -Recurse -Force
        $arrayEditor = Join-Path $Project 'Assets/Validation/ArrayCache/Editor'
        New-Item -ItemType Directory -Path $arrayEditor -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArrayCache/Editor/ArrayCacheCases.cs') -Destination $arrayEditor -Force
    } elseif ($Step -in @('TransSync', 'TransSyncVm')) {
        $destination = Join-Path $Project 'Assets/Validation/TransSync'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TransSync/Editor'),(Join-Path $PSScriptRoot 'TransSync/Runtime') -Destination $destination -Recurse -Force
    } elseif ($Step -eq 'TimelineRegression') {
        $destination = Join-Path $Project 'Assets/Validation/Timeline'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Timeline/Editor') -Destination $destination -Recurse -Force
    } elseif ($Step -in @('Configuration', 'TimelineVm')) {
        $destination = Join-Path $Project 'Assets/Validation/Configuration'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Configuration/Editor'),(Join-Path $PSScriptRoot 'Configuration/Runtime') -Destination $destination -Recurse -Force
    } elseif ($Step -in @('RpcDelivery', 'RpcQueueVm')) {
        $destination = Join-Path $Project 'Assets/Validation/RpcDelivery'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RpcDelivery/Editor'),(Join-Path $PSScriptRoot 'RpcDelivery/Runtime') -Destination $destination -Recurse -Force
    } elseif ($Step -eq 'NetworkEdges') {
        $destination = Join-Path $Project 'Assets/Validation/NetworkEdges'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NetworkEdges/Editor') -Destination $destination -Recurse -Force
    } elseif ($Step -eq 'EditorEncoding') {
        $destination = Join-Path $Project 'Assets/Validation/EditorEncoding'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorEncoding/Editor') -Destination $destination -Recurse -Force
    } elseif ($Step -in @('ArrayCache', 'UdonArrays')) {
        $destination = Join-Path $Project 'Assets/Validation/ArrayCache'
        $editor = Join-Path $destination 'Editor'
        New-Item -ItemType Directory -Path $editor -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArrayCache/Editor/ArrayCacheCases.cs') -Destination $editor -Force
        if ($Step -eq 'ArrayCache') {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArrayCache/Editor/DecoderArrayCacheValidation.cs') -Destination $editor -Force
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArrayCache/Runtime') -Destination $destination -Recurse -Force
        } else {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArrayCache/VRCEditor/DecoderArrayCacheUdonValidation.cs') -Destination $editor -Force
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArrayCache/Udon') -Destination $destination -Recurse -Force
        }
    } elseif ($Step -eq 'Workflow') {
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
        PrepareSdk = 'SdkValidationPreparation.Run'
        Udon = 'VrcSupportValidation.Compile'
        World = 'VrcSupportValidation.BuildWorld'
        Workflow = 'SharedWorkflowValidation.Run'
        ArrayCache = 'DecoderArrayCacheValidation.Run'
        UdonArrays = 'DecoderArrayCacheUdonValidation.Run'
        EditorEncoding = 'EditorEncodingValidation.Run'
        NetworkEdges = 'NetworkEdgeValidation.Run'
        RpcDelivery = 'RpcDeliveryValidation.Run'
        RpcQueueVm = 'RpcQueueVmValidation.Run'
        Configuration = 'ConfigurationValidation.Run'
        TimelineVm = 'ConfigurationValidation.RunTimelineVm'
        TimelineRegression = 'TimelineValidation.Run'
        TransSync = 'TransSyncSchedulingValidation.Run'
        TransSyncVm = 'TransSyncSchedulingValidation.RunVm'
        ReceivePolicies = 'ReceivePolicyValidation.Run'
        ReceivePoliciesVm = 'ReceivePolicyValidation.RunVm'
        PayloadIntegrity = 'PayloadIntegrityValidation.Run'
    }
    if ($Step -eq 'Inspect') {
        # Compile newly copied harness files in batch mode before opening a GUI
        # editor, where a script-error dialog could otherwise hold the run open.
        $compileLog = Join-Path $Results "$stamp-Inspect-compile-preflight.log"
        $compileArguments = "-projectPath `"$Project`" -batchmode -quit -logFile `"$compileLog`""
        $compileProcess = Start-Process -FilePath $UnityEditor -ArgumentList $compileArguments -WindowStyle Hidden -PassThru
        Write-Output "CompilePreflightPID=$($compileProcess.Id) Log=$compileLog"
        if (-not $compileProcess.WaitForExit($TimeoutSeconds * 1000)) {
            $compileProcess.Kill()
            throw "Inspector compilation preflight timed out; see $compileLog"
        }
        $compileProcess.Refresh()
        Assert-UnityScriptCompilation $compileLog $result
        if ($compileProcess.ExitCode -ne 0) { throw "Inspector compilation preflight exited $($compileProcess.ExitCode); see $compileLog" }
    }
    if ($Step -eq 'Player') {
        $arguments = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$log`""
        if ($PlayerMode -eq 'Batch') { $arguments += ' -batchmode' }
        $process = Start-Process -FilePath $build -ArgumentList $arguments -WindowStyle Hidden -PassThru
    } else {
        $arguments = "-projectPath `"$Project`" -logFile `"$log`""
        if ($Step -ne 'Inspect') { $arguments += ' -batchmode' }
        if ($Step -in @('Import', 'Build', 'InitializeSdk', 'Udon', 'ArrayCache', 'UdonArrays', 'EditorEncoding', 'NetworkEdges', 'RpcDelivery', 'Configuration', 'TimelineRegression', 'TransSync', 'ReceivePolicies', 'PayloadIntegrity')) { $arguments += ' -quit' }
        if ($methods.ContainsKey($Step)) { $arguments += ' -executeMethod ' + $methods[$Step] }
        if ($Step -eq 'Build') { New-Item -ItemType Directory -Path (Split-Path -Parent $build) -Force | Out-Null }
        $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    Write-Output "PID=$($process.Id) Log=$log Result=$result"
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        throw "Validation timed out; see $log"
    }
    $process.Refresh()
    Assert-UnityScriptCompilation $log $result
    if ($process.ExitCode -ne 0) { throw "Validation exited $($process.ExitCode); see $log" }
    if ($Step -in @('Play', 'Player', 'Inspect', 'PrepareSdk', 'Udon', 'World', 'Workflow', 'ArrayCache', 'UdonArrays', 'EditorEncoding', 'NetworkEdges', 'RpcDelivery', 'RpcQueueVm', 'Configuration', 'TimelineVm', 'TimelineRegression', 'TransSync', 'TransSyncVm', 'ReceivePolicies', 'ReceivePoliciesVm', 'PayloadIntegrity')) {
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
