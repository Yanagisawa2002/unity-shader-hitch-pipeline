[CmdletBinding()]
param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.5.2f1/Editor/Unity.exe',
    [Parameter(Mandatory = $true)][string]$SerializationScript,
    [string]$EvidenceRoot = '',
    [switch]$SkipBuild
)
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
& $SerializationScript -Action {
    if (!$SkipBuild) {
        Invoke-OwnedProcess $Unity @('-batchmode', '-quit', '-projectPath', ('"' + $project + '"'),
            '-buildTarget', 'Win64', '-executeMethod', 'StreamingFixtureBuilder.Build',
            '-stream-output', ('"' + $player + '"'), '-logFile', ('"' + (Join-Path $EvidenceRoot 'build.log') + '"'))
    }
    foreach ($mode in @('trace', 'smoke')) {
        Invoke-OwnedProcess $player @('-batchmode', '-force-d3d12', '-screen-fullscreen', '0',
            '-stream-mode', $mode, '-stream-evidence', ('"' + $EvidenceRoot + '"'),
            '-logFile', ('"' + (Join-Path $EvidenceRoot ($mode + '.log')) + '"'))
        $receipt = Get-Content -Raw (Join-Path $EvidenceRoot ($mode + '.json')) | ConvertFrom-Json
        if (!$receipt.passed) { throw "$mode fixture failed: $($receipt.error)" }
    }
    $hashes = @(Get-ChildItem $EvidenceRoot -File | Sort-Object Name | ForEach-Object {
        @{ path = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $provenance = @{
        schemaVersion = 1; scope = 'correctness smoke; not formal timing or OS capture';
        sourceCommit = (& git -C $repo rev-parse HEAD); sourceStatus = @(& git -C $repo status --short);
        unity = $Unity; player = $player; playerSha256 = (Get-FileHash $player -Algorithm SHA256).Hash.ToLowerInvariant();
        unitySha256 = (Get-FileHash $Unity -Algorithm SHA256).Hash.ToLowerInvariant();
        artifacts = $hashes; utc = [DateTime]::UtcNow.ToString('o')
    }
    $provenance | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $EvidenceRoot 'provenance.json')
    Write-Output "ADDRESSABLES_SMOKE_OK $EvidenceRoot"
}
