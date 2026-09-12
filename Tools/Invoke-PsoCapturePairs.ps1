[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Declaration,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$SerializedRunner,
    [Parameter(Mandatory)][string]$Python
)
$ErrorActionPreference = 'Stop'
$config = Get-Content -Raw -LiteralPath $Declaration | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $root) { throw 'A fresh output directory is required.' }
if ($config.schemaVersion -ne 1 -or $config.pairs -ne 5 -or $config.prePlayerWaitSeconds -ne 20 -or $config.captureSeconds -ne 180 -or $config.playerTimeoutSeconds -ne 150) { throw 'Fixed five-pair capture protocol required.' }
$build = Get-Content -Raw -LiteralPath $config.buildManifest | ConvertFrom-Json
if ($build.sourceDirty -ne $false -or $build.sourceRevision -notmatch '^[a-f0-9]{40}$' -or $build.expectedBenchmarkFrames -ne 7200 -or $build.expectedPhaseStates.startup -ne 12 -or $build.expectedPhaseStates.combat -ne 388) { throw 'Exact workload/build attestation required.' }
if ((& git -C (Join-Path $PSScriptRoot '..') rev-parse HEAD) -ne $build.sourceRevision -or (& git -C (Join-Path $PSScriptRoot '..') status --porcelain)) { throw 'Capture source must be the clean frozen commit.' }
if ((Get-FileHash -LiteralPath $config.player).Hash.ToLowerInvariant() -ne $build.playerSha256) { throw 'Player hash mismatch.' }
foreach ($flag in @('-pso-output','-pso-warmup-receipt','-pso-benchmark-report','-pso-system-markers','-logFile')) { if ($config.playerArguments -contains $flag) { throw "Reserved argument $flag" } }
$expected = @('pm','wpr','wpr','pm','pm','wpr','wpr','pm','pm','wpr')
if (($config.order -join ',') -ne ($expected -join ',')) { throw 'Frozen interleaving differs.' }
if ((Get-PSDrive -Name ([IO.Path]::GetPathRoot($root).Substring(0,1))).Free -lt 60GB) { throw 'At least 60 GiB free is required before this finite five-ETL matrix.' }
New-Item -ItemType Directory -Path $root | Out-Null
Copy-Item -LiteralPath $Declaration -Destination (Join-Path $root 'declaration.json')
$attempts = [Collections.Generic.List[object]]::new()
& $SerializedRunner -Action {
    for ($index=0; $index -lt 10; $index++) {
        if ((Get-PSDrive C).Free -lt 20GB) { throw 'Storage reserve reached; stop, retain prior attempts, no automatic rerun.' }
        $pair = [int][Math]::Floor($index/2)+1
        $arm = $expected[$index]
        $run = Join-Path $root ('pair-'+$pair+'-'+$arm)
        New-Item -ItemType Directory -Path $run | Out-Null
        $warmup=Join-Path $run 'scheduled.warmup.json'
        $benchmark=Join-Path $run 'scheduled.benchmark.json'
        $markers=Join-Path $run 'system-markers.jsonl'
        $arguments=@($config.playerArguments)+@('-pso-output',$run,'-pso-warmup-receipt',$warmup,'-pso-benchmark-report',$benchmark,'-pso-system-markers',$markers,'-logFile',(Join-Path $run 'player.log'))
        $errorText=''
        try {
            & (Join-Path $PSScriptRoot 'Invoke-PsoWindowsEvidence.ps1') -Player $config.player -PresentMon $config.presentMon -BuildManifest $config.buildManifest `
                -PlayerArguments $arguments -WarmupReceipt $warmup -BenchmarkReceipt $benchmark -Markers $markers -Output (Join-Path $run 'windows') `
                -CaptureEtw:($arm -eq 'wpr') -CaptureSeconds 180 -PlayerTimeoutSeconds 150 -PrePlayerWaitSeconds 20 -Python $Python
        } catch { $errorText=$_.Exception.ToString() }
        $attempts.Add(@{pair=$pair;arm=$arm;order=$index;directory=$run;error=$errorText})
        $attempts | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'attempts.json') -Encoding utf8NoBOM
    }
}
Write-Output "PAIR_CAPTURE_FINISHED $root"
