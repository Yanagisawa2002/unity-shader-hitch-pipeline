#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Protocol)
$ErrorActionPreference = 'Stop'
$protocolPath = [IO.Path]::GetFullPath($Protocol)
$root = Split-Path $protocolPath
$expected = Get-Content -Raw (Join-Path $root 'freeze-receipt.json') | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $protocolPath).Hash.ToLowerInvariant() -ne $expected.protocolSha256 -or -not $expected.frozenBeforeRuns) {
    throw 'Frozen protocol changed'
}
$cell = Get-Content -Raw -LiteralPath $protocolPath | ConvertFrom-Json
if ($cell.upstreamCommit -ne '07652ee74a1f322c2c3e607020f07be720175680' -or
    $cell.upstreamTree -ne '7b0bf700ebc01face91223fd7185a02f44ec376e') { throw 'Wrong pinned official workload' }
$playerRoot = Split-Path $cell.player
$indexPath = Join-Path $root 'player-files.json'
if ((Get-FileHash -LiteralPath $indexPath).Hash.ToLowerInvariant() -ne $cell.playerIndexSha256) { throw 'Frozen Player index changed' }
$files = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
foreach ($run in $cell.runs) {
    $stage = Join-Path $root $run.directory
    if (Test-Path -LiteralPath $stage) { throw 'Retain previous attempt; this is not a resampling/resume helper' }
    & (Join-Path $PSScriptRoot 'Invoke-PsoNativeStage.ps1') -Output (Join-Path $root ('artifact-prerun-' + $run.index)) -BudgetPath $playerRoot -EstimatedAdditionalPeakGiB 1 -Action {
        if ((Get-FileHash -LiteralPath $protocolPath).Hash.ToLowerInvariant() -ne $expected.protocolSha256) { throw 'Protocol changed during sequence' }
        foreach ($tool in $cell.executionTools) {
            if ([IO.Path]::GetFileName($tool.file) -cne $tool.file -or
                (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $tool.file)).Hash.ToLowerInvariant() -ne $tool.sha256) { throw 'Frozen execution dependency changed' }
        }
        if ((Get-FileHash (Join-Path $PSScriptRoot 'pso_megacity_capture.py')).Hash.ToLowerInvariant() -ne $cell.validationToolSha256 -or
            (Get-FileHash (Join-Path $PSScriptRoot 'pso_megacity_comparison.py')).Hash.ToLowerInvariant() -ne $cell.protocolToolSha256) { throw 'Frozen validation/protocol tools changed' }
        $required = @('Megacity.exe','GameAssembly.dll','UnityPlayer.dll','Megacity_Data/StreamingAssets/ShaderHitchPipeline/plan.json')
        $selected = @($files | Where-Object { $_.path -in $required })
        if ($selected.Count -ne $required.Count) { throw 'Frozen Player lacks required packaged inputs' }
        foreach ($file in $selected) {
            if ((Get-FileHash -LiteralPath (Join-Path $playerRoot $file.path)).Hash.ToLowerInvariant() -ne $file.sha256) { throw 'Frozen Player input changed' }
        }
    }
    & (Join-Path $PSScriptRoot 'Invoke-PsoExternalPlayer.ps1') -Player $cell.player -Output $stage -Session $run.directory `
        -Policy $run.policy -PlanBaselineForDisabled -MaximumSeconds $cell.maximumProcessSeconds `
        -ExtraArguments @('-pso-megacity-single-player','-pso-megacity-observe-seconds',([string]$cell.observationSeconds))
    & (Join-Path $PSScriptRoot 'Invoke-PsoNativeStage.ps1') -Output (Join-Path $stage 'validation-stage') -BudgetPath $playerRoot -EstimatedAdditionalPeakGiB 1 -Action {
        & python (Join-Path $PSScriptRoot 'pso_megacity_capture.py') --stage $stage --output (Join-Path $stage 'validation.json') --require-warmup
        if ($LASTEXITCODE -ne 0) { throw 'Formal gate failed; remaining runs unexecuted, no replacement' }
        $result = Get-Content -Raw (Join-Path $stage 'validation.json') | ConvertFrom-Json
        if (-not $result.accepted -or -not $result.contentAccepted -or -not $result.nativePolicyValidated -or
            $result.buildGuid -ne $cell.buildGuid -or $result.policy -ne $run.policy -or
            $result.screenshotsEnabled -or $result.traceEnabled -or $result.cacheCondition -ne $cell.processCacheCondition -or
            $result.requestedObservationSeconds -ne $cell.observationSeconds) { throw 'Actual formal modes/content/build differ' }
        foreach ($key in @('unityVersion','buildGuid','graphicsDeviceType','graphicsDeviceName','driverIdentity','driverVersion','qualityLevelName','renderingThreadingMode','processorCount','identity')) {
            if (($result.environment.$key | ConvertTo-Json -Depth 8 -Compress) -cne ($cell.environment.$key | ConvertTo-Json -Depth 8 -Compress)) { throw "Formal runtime identity changed: $key" }
        }
        foreach ($key in @('planFileSha256','planContentSha256','collections')) {
            if (($result.installedPlanBinding.$key | ConvertTo-Json -Depth 8 -Compress) -cne ($cell.$key | ConvertTo-Json -Depth 8 -Compress)) { throw "Actual installed plan binding differs: $key" }
        }
        if (($result.renderSettings | ConvertTo-Json -Compress) -cne ($cell.renderSettings | ConvertTo-Json -Compress)) { throw 'Actual VSync/target frame rate changed' }
        Write-Output ('FORMAL_ACCEPTED index={0} policy={1} seconds={2:N3}' -f $run.index,$run.policy,$result.observerElapsedSeconds)
    }
}
& (Join-Path $PSScriptRoot 'Invoke-PsoNativeStage.ps1') -Output (Join-Path $root 'artifact-postcheck') -BudgetPath $playerRoot -EstimatedAdditionalPeakGiB 1 -Action {
    & python (Join-Path $PSScriptRoot 'pso_megacity_comparison.py') verify-player $root
    if ($LASTEXITCODE -ne 0) { throw 'Actual final Player verification failed' }
    & python (Join-Path $PSScriptRoot 'pso_megacity_comparison.py') summarize $root
    if ($LASTEXITCODE -ne 0) { throw 'Raw comparison verification failed' }
}
