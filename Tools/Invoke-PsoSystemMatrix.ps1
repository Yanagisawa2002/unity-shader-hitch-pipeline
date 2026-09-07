[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Declaration,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$SerializedRunner,
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$declarationPath = (Resolve-Path -LiteralPath $Declaration).Path
$matrixConfig = Get-Content -Raw -LiteralPath $declarationPath | ConvertFrom-Json
if ($matrixConfig.schemaVersion -ne 1 -or $matrixConfig.repetitions -ne 5 -or $matrixConfig.cacheState -ne 'process-cold' -or -not $matrixConfig.workloadId) {
    throw 'Declare schemaVersion=1, repetitions=5, cacheState=process-cold and workloadId before running.'
}
$matrixRoot = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $matrixRoot) { throw 'Use a fresh matrix output directory.' }
foreach ($required in @($matrixConfig.player,$matrixConfig.presentMon,$matrixConfig.buildManifest,$SerializedRunner)) {
    if (-not $required -or -not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required input missing: $required" }
}
$build = Get-Content -Raw -LiteralPath $matrixConfig.buildManifest | ConvertFrom-Json
if ($build.expectedBenchmarkFrames -lt 1 -or -not $build.expectedPhaseStates -or $matrixConfig.playerArguments -notcontains '-pso-benchmark') { throw 'Declare the real benchmark frame count and exact phase/state workload in buildManifest; -pso-benchmark is required.' }
if ($build.workloadId -ne $matrixConfig.workloadId -or $build.sourceDirty -ne $false -or $build.sourceRevision -notmatch '^[0-9a-f]{40}$' -or $build.playerSha256 -ne (Get-FileHash -LiteralPath $matrixConfig.player -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'Build attestation must bind the clean source revision, workloadId, and exact Player hash.'
}
# Fixed paths are appended by the runner; prevent accidental old/duplicate receipt flags.
foreach ($reserved in @('-pso-warmup-receipt','-pso-benchmark-report','-pso-system-markers','-logFile','-pso-output')) {
    if ($matrixConfig.playerArguments -contains $reserved) { throw "Reserved runner argument: $reserved" }
}
New-Item -ItemType Directory -Path $matrixRoot | Out-Null
Copy-Item -LiteralPath $declarationPath -Destination (Join-Path $matrixRoot 'declaration.json')
$captureScript = Join-Path $PSScriptRoot 'Invoke-PsoWindowsEvidence.ps1'
$acceptanceScript = Join-Path $PSScriptRoot 'pso_system_acceptance.py'
$results = [Collections.Generic.List[object]]::new()
& $SerializedRunner -Action {
    for ($run = 1; $run -le 5; $run++) {
        $runDirectory = Join-Path $matrixRoot "run-$run"
        New-Item -ItemType Directory -Path $runDirectory | Out-Null
        $warmup = Join-Path $runDirectory 'scheduled.warmup.json'
        $benchmark = Join-Path $runDirectory 'scheduled.benchmark.json'
        $markers = Join-Path $runDirectory 'system-markers.jsonl'
        $arguments = @($matrixConfig.playerArguments | ForEach-Object { $_.Replace('{runDirectory}',$runDirectory) })
        $arguments += @('-pso-output',$runDirectory,'-pso-warmup-receipt',$warmup,'-pso-benchmark-report',$benchmark,'-pso-system-markers',$markers,'-logFile',(Join-Path $runDirectory 'player.log'))
        $failure = ''
        try {
            & $captureScript -Player $matrixConfig.player -PresentMon $matrixConfig.presentMon -BuildManifest $matrixConfig.buildManifest `
                -PlayerArguments $arguments -WarmupReceipt $warmup -BenchmarkReceipt $benchmark -Markers $markers `
                -Output (Join-Path $runDirectory 'windows') -CaptureEtw:($run -eq 1) -CaptureUnavailableContinueEngine `
                -CaptureSeconds $matrixConfig.captureSeconds -PlayerTimeoutSeconds $matrixConfig.playerTimeoutSeconds -Python $Python
        } catch { $failure = $_.Exception.ToString() }
        $results.Add(@{run=$run; captureError=$failure; directory=$runDirectory})
        # Always retain all five independent attempts, including denied capture logs.
        $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $matrixRoot 'attempts.json') -Encoding utf8NoBOM
    }
}
& $Python $acceptanceScript --runs $matrixRoot --output (Join-Path $matrixRoot 'system-acceptance.json')
if ($LASTEXITCODE -ne 0) { throw 'Required Windows system evidence gate unmet; see system-acceptance.json and all five retained attempts.' }
