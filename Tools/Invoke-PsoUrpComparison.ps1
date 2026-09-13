#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Protocol)
$ErrorActionPreference='Stop'
$protocolPath=[IO.Path]::GetFullPath($Protocol)
$root=Split-Path $protocolPath
$expected=Get-Content -Raw (Join-Path $root 'freeze-receipt.json') | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $protocolPath).Hash.ToLowerInvariant() -ne $expected.protocolSha256 -or -not $expected.frozenBeforeRuns) { throw 'Frozen protocol changed' }
$cell=Get-Content -Raw -LiteralPath $protocolPath | ConvertFrom-Json
if ($cell.sourceArchiveSha256 -ne 'c2a2bcbd8bac1340be683b33ca53e1fddc78c2ae80fa3a0b5406bd616df6628f') { throw 'Wrong pinned official workload' }
$indexPath=Join-Path $root 'player-files.json'
if ((Get-FileHash -LiteralPath $indexPath).Hash.ToLowerInvariant() -ne $cell.playerIndexSha256) { throw 'Frozen Player index changed' }
$files=Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
$playerRoot=Split-Path $cell.player
foreach ($run in $cell.runs) {
    $stage=Join-Path $root $run.directory
    if (Test-Path -LiteralPath $stage) { throw 'Retain previous attempt; this is not a resampling/resume helper' }
    foreach ($file in $files | Where-Object { $_.path -in @('UrpExternal.exe','GameAssembly.dll','UnityPlayer.dll','UrpExternal_Data/StreamingAssets/ShaderHitchPipeline/plan.json') }) {
        if ((Get-FileHash -LiteralPath (Join-Path $playerRoot $file.path)).Hash.ToLowerInvariant() -ne $file.sha256) { throw 'Frozen Player input changed' }
    }
    & (Join-Path $PSScriptRoot 'Invoke-PsoExternalPlayer.ps1') -Player $cell.player -Output $stage -Session $run.directory -Policy $run.policy -PlanBaselineForDisabled -MaximumSeconds $cell.maximumProcessSeconds
    & python (Join-Path $PSScriptRoot 'pso_urp_capture.py') $stage --output (Join-Path $stage 'validation.json') --require-warmup
    if ($LASTEXITCODE -ne 0) { throw 'Formal gate failed; remaining runs unexecuted, no replacement' }
    $result=Get-Content -Raw (Join-Path $stage 'validation.json') | ConvertFrom-Json
    if ($result.buildGuid -ne $cell.buildGuid -or $result.policy -ne $run.policy -or -not $result.nativePolicyValidated -or $result.screenshotsEnabled -or $result.traceEnabled) { throw 'Actual formal modes/build differ' }
    $actualEnvironment=$result.warmupReceipts[0].data.environment
    foreach ($key in @('unityVersion','buildGuid','graphicsDeviceType','graphicsDeviceName','driverIdentity','driverVersion','qualityLevelName','renderingThreadingMode','processorCount','identity')) {
        if (($actualEnvironment.$key | ConvertTo-Json -Depth 8 -Compress) -cne ($cell.environment.$key | ConvertTo-Json -Depth 8 -Compress)) { throw "Formal runtime identity changed: $key" }
    }
    if ($result.warmupReceipts[0].data.planSha256 -ne $cell.planFileSha256) { throw 'Runtime plan differs' }
    Write-Output ('FORMAL_ACCEPTED index={0} policy={1} seconds={2:N3}' -f $run.index,$run.policy,$result.observerElapsedSeconds)
}
& (Join-Path $PSScriptRoot 'Invoke-PsoNativeStage.ps1') -Output (Join-Path $root 'artifact-postcheck') -BudgetPath $playerRoot -EstimatedAdditionalPeakGiB 1 -Action {
    & python (Join-Path $PSScriptRoot 'pso_urp_comparison.py') verify-player $root
    if ($LASTEXITCODE -ne 0) { throw 'Actual Player verification failed' }
    & python (Join-Path $PSScriptRoot 'pso_urp_comparison.py') summarize $root
    if ($LASTEXITCODE -ne 0) { throw 'Raw comparison verification failed' }
}
