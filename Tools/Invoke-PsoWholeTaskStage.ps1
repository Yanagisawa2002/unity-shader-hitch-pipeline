#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][scriptblock]$Work,
    [ValidateRange(0,1000)][double]$EstimatedAdditionalPeakGiB=80,
    [string]$Coordination='D:/CodexWork/whole-task-validation-20260915/coordination'
)
# One synchronous, explicitly invoked stage. No polling or automatic job queue.
$ErrorActionPreference='Stop'
$campaignRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$campaignOutput=[IO.Path]::GetFullPath($Output)
$relative=[IO.Path]::GetRelativePath((Join-Path $campaignRepo 'work'),$campaignOutput)
if ($relative.StartsWith('..') -or [IO.Path]::IsPathRooted($relative)) { throw 'Whole-task evidence must be under this checkout/work.' }
$queue=Get-Content -Raw -LiteralPath (Join-Path $Coordination 'queue.json') | ConvertFrom-Json
if (($queue.order -join ',') -cne 'hlsl,summit,data-layout,shader' -or $queue.mutex -cne 'Local\CodexR9700VNextUnityGpu') {
    throw 'Unexpected campaign order or mutex.'
}
$predecessors=@()
foreach ($id in @('hlsl','summit','data-layout')) {
    $path=Join-Path $Coordination "handoff/$id.json"
    $handoff=Get-Content -Raw -LiteralPath $path -ErrorAction Stop | ConvertFrom-Json
    if ($handoff.id -cne $id -or $handoff.terminal -isnot [bool] -or -not $handoff.terminal -or
        $handoff.hardwareReleased -isnot [bool] -or -not $handoff.hardwareReleased) {
        throw "Predecessor $id has not provided a valid terminal hardware release."
    }
    $predecessors+=@{ id=$id; sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant(); record=$handoff }
}
$campaignTemp=Join-Path $campaignRepo 'work/task-temp'
New-Item -ItemType Directory -Path $campaignTemp -Force | Out-Null
$oldTaskTemp=$env:TEMP; $oldTaskTmp=$env:TMP
$oldUpmCache=$env:UPM_CACHE_ROOT; $oldNugetCache=$env:NUGET_PACKAGES
try {
    # Process-local temporary files stay on the task volume. No global setting,
    # application cache, driver cache or existing Editor is modified.
    $env:TEMP=$campaignTemp; $env:TMP=$campaignTemp
    $env:UPM_CACHE_ROOT=Join-Path $campaignRepo 'work/upm-cache'
    $env:NUGET_PACKAGES=Join-Path $campaignRepo 'work/nuget-packages'
    & (Join-Path $PSScriptRoot 'Invoke-PsoNativeStage.ps1') -Output $campaignOutput -BudgetPath $campaignRepo -EstimatedAdditionalPeakGiB $EstimatedAdditionalPeakGiB -Action {
        $predecessors | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $campaignOutput 'predecessors.json') -Encoding utf8
        $loadSamples=@()
        $gpuCommand=Get-Command nvidia-smi -ErrorAction Stop
        for ($i=0;$i -lt 5;$i++) {
            $cpu=Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'"
            $gpuLines=@(& $gpuCommand.Source '--query-gpu=uuid,name,driver_version,utilization.gpu,memory.used' '--format=csv,noheader,nounits')
            if ($LASTEXITCODE -ne 0) { throw 'GPU load probe failed; no heavy work started.' }
            $gpus=@($gpuLines | ConvertFrom-Csv -Header uuid,name,driver,utilizationPercent,memoryMiB)
            foreach ($gpu in $gpus) { $gpu.utilizationPercent=[double]$gpu.utilizationPercent }
            $loadSamples+=@{ utc=[DateTimeOffset]::UtcNow.ToString('o'); cpuPercent=[double]$cpu.PercentProcessorTime; gpus=$gpus }
            if ($i -lt 4) { Start-Sleep -Seconds 1 }
        }
        $loadReceipt=@{ samples=$loadSamples; maxAllowedCpuPercent=20; maxAllowedGpuPercent=10;
            unavailableIsFailure=$true; scope='Preflight observations; not performance measurements.' }
        $loadReceipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $campaignOutput 'load-preflight.json') -Encoding utf8
        if (@($loadSamples | Where-Object { $_.cpuPercent -gt 20 -or @($_.gpus | Where-Object { $_.utilizationPercent -gt 10 }).Count }).Count) {
            throw 'Background load exceeds predeclared CPU 20% / GPU 10% gate. Leave other processes untouched.'
        }
        & $Work
    }
} finally {
    $env:TEMP=$oldTaskTemp; $env:TMP=$oldTaskTmp
    $env:UPM_CACHE_ROOT=$oldUpmCache; $env:NUGET_PACKAGES=$oldNugetCache
}
