#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Protocol)
$ErrorActionPreference='Stop'
$protocolPath=[IO.Path]::GetFullPath($Protocol)
$root=Split-Path $protocolPath
$expected=Get-Content -Raw (Join-Path $root 'freeze-receipt.json') | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $protocolPath).Hash.ToLowerInvariant() -ne $expected.protocolSha256) { throw 'Frozen protocol changed' }
$cell=Get-Content -Raw -LiteralPath $protocolPath | ConvertFrom-Json
$files=Get-Content -Raw (Join-Path $root 'player-files.json') | ConvertFrom-Json
$playerRoot=Split-Path $cell.player
foreach ($run in $cell.runs) {
    $stage=Join-Path $root $run.directory
    if (Test-Path -LiteralPath $stage) { throw 'Retain previous attempt; this sequence is not a resampling/resume helper' }
    foreach ($file in $files | Where-Object { $_.path -in @('BoatAttack.exe','GameAssembly.dll','UnityPlayer.dll','BoatAttack_Data/StreamingAssets/ShaderHitchPipeline/plan.json') }) {
        if ((Get-FileHash -LiteralPath (Join-Path $playerRoot $file.path)).Hash.ToLowerInvariant() -ne $file.sha256) { throw 'Frozen Player input changed' }
    }
    & (Join-Path $PSScriptRoot 'Invoke-PsoExternalPlayer.ps1') -Player $cell.player -Output $stage -Session $run.directory -Policy $run.policy -PlanBaselineForDisabled -MaximumSeconds $cell.maximumProcessSeconds
    & python (Join-Path $PSScriptRoot 'pso_external_capture.py') $stage --output (Join-Path $stage 'validation.json') --require-warmup --require-no-leaks
    if ($LASTEXITCODE -ne 0) { throw 'Formal gate failed; remaining runs are unexecuted, no sample replacement' }
    $result=Get-Content -Raw (Join-Path $stage 'validation.json') | ConvertFrom-Json
    if ($result.buildGuid -ne $cell.buildGuid) { throw 'Unexpected actual runtime build GUID' }
    $actualEnvironment=$result.warmupReceipts[0].data.environment
    foreach ($key in @('unityVersion','buildGuid','graphicsDeviceType','graphicsDeviceName','driverIdentity','driverVersion','qualityLevelName','renderingThreadingMode','processorCount','identity')) {
        if (($actualEnvironment.$key | ConvertTo-Json -Depth 8 -Compress) -cne ($cell.environment.$key | ConvertTo-Json -Depth 8 -Compress)) {
            throw "Formal runtime identity changed: $key"
        }
    }
    if ($result.warmupReceipts[0].data.planSha256 -ne $cell.planFileSha256) { throw 'Runtime plan file hash differs from protocol' }
    Write-Output ('FORMAL_ACCEPTED index={0} policy={1} seconds={2:N3}' -f $run.index,$run.policy,$result.observerElapsedSeconds)
}
foreach ($file in $files) {
    $actual=Join-Path $playerRoot $file.path
    if ((Get-Item -LiteralPath $actual).Length -ne $file.bytes -or (Get-FileHash -LiteralPath $actual).Hash.ToLowerInvariant() -ne $file.sha256) { throw 'Player artifact changed during comparison' }
}
@{ verifiedUtc=[DateTime]::UtcNow.ToString('o'); files=$files.Count; playerUnchanged=$true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'player-after.json') -Encoding utf8
& python (Join-Path $PSScriptRoot 'pso_boat_comparison.py') summarize $root
if ($LASTEXITCODE -ne 0) { throw 'Comparison summary failed' }
