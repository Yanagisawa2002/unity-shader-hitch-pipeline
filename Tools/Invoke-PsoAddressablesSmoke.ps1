[CmdletBinding()]
param(
    [switch]$AllowPerformanceExecution,
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.5.2f1/Editor/Unity.exe',
    [Parameter(Mandatory = $true)][string]$SerializationScript,
    [string]$EvidenceRoot = '',
    [switch]$SkipBuild,
    [switch]$SmokeOnly,
    [string]$SeedTraceRoot = ''
)
. (Join-Path $PSScriptRoot 'PsoExecutionPolicy.ps1')
Assert-PsoRuntimeExecutionAllowed -AllowPerformanceExecution:$AllowPerformanceExecution

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'Integrations/Addressables/UnityProject'
if (!$EvidenceRoot) { $EvidenceRoot = Join-Path $repo ('Evidence/Local/addressables-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$player = Join-Path $project 'Builds/StreamingFixture.exe'
New-Item -ItemType Directory -Force $EvidenceRoot | Out-Null
function Invoke-OwnedProcess([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "$Executable failed with exit code $($process.ExitCode); see logs in $EvidenceRoot" }
}
function Invoke-FixtureBuild([string]$Log, [string]$Source = '') {
    $buildArguments = @('-batchmode', '-quit', '-pso-training-build', '-projectPath', ('"' + $project + '"'),
        '-buildTarget', 'Win64', '-executeMethod', 'StreamingFixtureBuilder.Build',
        '-stream-output', ('"' + $player + '"'), '-logFile', ('"' + $Log + '"'))
    if ($Source) { $buildArguments += @('-stream-collections-source', ('"' + $Source + '"')) }
    Invoke-OwnedProcess $Unity $buildArguments
}
function Invoke-FixtureRun([string]$Mode, [string]$Directory, [bool]$VerifyNative) {
    New-Item -ItemType Directory -Force $Directory | Out-Null
    $runArguments = @('-batchmode', '-force-d3d12', '-screen-fullscreen', '0',
        '-stream-mode', $Mode, '-stream-evidence', ('"' + $Directory + '"'),
        '-logFile', ('"' + (Join-Path $Directory ($Mode + '.log')) + '"'))
    if ($VerifyNative) { $runArguments += '-stream-verify-native' }
    Invoke-OwnedProcess $player $runArguments
    $receipt = Get-Content -Raw (Join-Path $Directory ($Mode + '.json')) | ConvertFrom-Json
    if (!$receipt.passed) { throw "$Mode fixture failed: $($receipt.error)" }
}
& $SerializationScript -Action {
    if (!$SkipBuild -and !$SmokeOnly) {
        if (!$SeedTraceRoot) {
            $SeedTraceRoot = Join-Path $EvidenceRoot 'seed'
            Invoke-FixtureBuild (Join-Path $EvidenceRoot 'seed-build.log')
            Invoke-FixtureRun 'trace' $SeedTraceRoot $false
        }
        Invoke-FixtureBuild (Join-Path $EvidenceRoot 'build.log') $SeedTraceRoot
    }
    if (!$SmokeOnly) { Invoke-FixtureRun 'trace' $EvidenceRoot $true }
    Invoke-FixtureRun 'smoke' $EvidenceRoot $true
    $hashes = @(Get-ChildItem $EvidenceRoot -File | Where-Object { $_.Name -ne 'provenance.json' } | Sort-Object Name | ForEach-Object {
        @{ path = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $playerDirectory = Split-Path $player
    $playerFiles = @(Get-ChildItem $playerDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{ path = $_.FullName.Substring($playerDirectory.Length + 1).Replace('\', '/');
           sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $provenance = @{
        schemaVersion = 1; scope = 'correctness smoke; not formal timing or OS capture';
        sourceCommit = (& git -C $repo rev-parse HEAD); sourceStatus = @(& git -C $repo status --short); seedTraceRoot = $SeedTraceRoot;
        unity = $Unity; player = $player; playerSha256 = (Get-FileHash $player -Algorithm SHA256).Hash.ToLowerInvariant();
        unitySha256 = (Get-FileHash $Unity -Algorithm SHA256).Hash.ToLowerInvariant();
        artifacts = $hashes; playerFiles = $playerFiles; utc = [DateTime]::UtcNow.ToString('o')
    }
    $provenance | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $EvidenceRoot 'provenance.json')
    Write-Output "ADDRESSABLES_SMOKE_OK $EvidenceRoot"
}
