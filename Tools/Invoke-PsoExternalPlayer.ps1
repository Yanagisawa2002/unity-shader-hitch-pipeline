#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Player,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$Session,
    [ValidateSet('disabled','all-at-once','scheduled','observed-budget')][string]$Policy = 'disabled',
    [switch]$Screenshots,
    [switch]$Trace,
    [switch]$PlanBaselineForDisabled,
    [string]$TracePhase = 'startup',
    [string[]]$ExtraArguments = @(),
    [int]$MaximumSeconds = 600
)
$ErrorActionPreference = 'Stop'
$binary = [IO.Path]::GetFullPath($Player)
$stage = [IO.Path]::GetFullPath($Output)
if (-not (Test-Path -LiteralPath $binary)) { throw 'A completed Player is required.' }
if ($Policy -eq 'disabled' -and $PlanBaselineForDisabled) {
    $data = [IO.Path]::GetFileNameWithoutExtension($binary) + '_Data'
    $planPath = Join-Path (Split-Path $binary) "$data/StreamingAssets/ShaderHitchPipeline/plan.json"
    $plan = Get-Content -Raw -LiteralPath $planPath | ConvertFrom-Json
    if (@($plan.phases | Where-Object prewarmAtStartup).Count) {
        throw 'Baseline-only disabled arm requires a plan with no automatic startup work.'
    }
}
& (Join-Path $PSScriptRoot 'Invoke-PsoNativeStage.ps1') -Output $stage -BudgetPath $binary -EstimatedAdditionalPeakGiB 2 -Action {
    $capture = Join-Path $stage 'capture'
    $log = Join-Path $stage 'player.log'
    $arguments = @('-force-d3d12', '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080',
        '-logFile', ('"' + $log + '"'), '-pso-external-capture', '-pso-output', ('"' + $capture + '"'),
        '-pso-session', $Session)
    if ($Trace) { $arguments += @('-pso-trace', '-pso-trace-phase', $TracePhase) }
    if ($Screenshots) { $arguments += '-pso-external-screenshots' }
    if ($Policy -eq 'disabled') {
        # Final comparison can retain the same plan-baseline observation cost in
        # every arm. Its adapter renders cold and must attest zero activations.
        $arguments += $(if ($PlanBaselineForDisabled) { '-pso-external-render-cold' } else { '-pso-disable-warmup' })
    }
    else {
        $strategy = if ($Policy -eq 'all-at-once') { 'throughput' } else { $Policy }
        $arguments += @('-pso-warmup-strategy', $strategy)
    }
    $arguments += $ExtraArguments
    [ordered]@{ startedUtc = [DateTime]::UtcNow.ToString('o'); player = $binary;
        sha256 = (Get-FileHash -LiteralPath $binary).Hash.ToLowerInvariant(); arguments = $arguments;
        policy = $Policy; screenshots = [bool]$Screenshots; trace = [bool]$Trace;
        planBaselineForDisabled = [bool]$PlanBaselineForDisabled;
        windowMode = 'Normal visible window, 1920x1080 windowed; real render/content acceptance';
        cacheCondition = 'New process, existing application/OS/driver caches retained. Not driver-cold.' } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $stage 'command.json') -Encoding utf8
    # The actual rendering application must present its content for acceptance.
    # Hiding its swapchain is not equivalent to hiding the monitoring helper.
    $child = Start-Process -FilePath $binary -ArgumentList $arguments -WorkingDirectory (Split-Path $binary) -WindowStyle Normal -PassThru
    & (Join-Path $PSScriptRoot 'Wait-PsoOwnedProcess.ps1') -Process $child -EvidenceDirectory $stage -BudgetPath $binary -LogPath $log -MaximumSeconds $MaximumSeconds
    if (-not (Test-Path (Join-Path $capture 'external-capture.json'))) { throw 'Player exited without external capture; correctness not established.' }
}
