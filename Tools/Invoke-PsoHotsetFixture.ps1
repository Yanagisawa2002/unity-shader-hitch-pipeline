[CmdletBinding()]
param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.5.2f1/Editor/Unity.exe',
    [string]$ProjectPath = (Join-Path $PSScriptRoot '../UnityProject'),
    [string]$OutputRoot = '',
    [ValidateRange(1,3)][int]$Repetitions = 1,
    [switch]$SkipBuild,
    [switch]$BuildOnly,
    [switch]$NextHeldOut
)
$ErrorActionPreference = 'Stop'
# Caller must hold Invoke-SerializedValidation.ps1's shared mutex for the whole call.
$project = [IO.Path]::GetFullPath($ProjectPath)
if (!$OutputRoot) { $OutputRoot = Join-Path $project ('PsoArtifacts/Hotset/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$runRoot = [IO.Path]::GetFullPath($OutputRoot)
if (!$BuildOnly -and (Test-Path (Join-Path $runRoot 'frozen-policy.json'))) { throw 'Use a fresh experiment directory; frozen evidence must not be overwritten.' }
New-Item -ItemType Directory -Force $runRoot | Out-Null
$player = Join-Path $project 'Builds/Hotset/Hotset.exe'
function Invoke-OwnedProcess([string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $style = if ($Executable -eq $player) { 'Normal' } else { 'Hidden' }
    $taskProcess = Start-Process -FilePath $Executable -ArgumentList $Arguments -PassThru -WindowStyle $style
    try {
        if (!$taskProcess.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $taskProcess.Id -Force
            $taskProcess.WaitForExit()
            throw "Owned process timed out: $Executable"
        }
        if ($taskProcess.ExitCode -ne 0) { throw "Process failed ($($taskProcess.ExitCode)): $Executable" }
    } finally { $taskProcess.Dispose() }
}
if (!$SkipBuild) {
    Invoke-OwnedProcess $Unity @('-batchmode','-nographics','-quit','-projectPath',('"'+$project+'"'),
        '-executeMethod','Yanagisawa.ShaderHitchPipeline.HotsetFixture.PsoHotsetFixtureBuilder.Build',
        '-pso-training-build','-hotset-player',('"'+$player+'"'),'-logFile',('"'+(Join-Path $runRoot 'build.log')+'"')) 1200
}
if ($BuildOnly) { Write-Output "HOTSET_BUILD_OK $player"; return }
if (!(Test-Path $player)) { throw 'Player missing' }
function Get-FixtureAdapter {
    $adapters = @(Get-CimInstance Win32_VideoController | Where-Object { $_.Name -like '*R9700*' })
    if ($adapters.Count -ne 1 -or !$adapters[0].DriverVersion) { throw 'Exactly one observed R9700 driver identity required' }
    return @{name=$adapters[0].Name;driverVersion=$adapters[0].DriverVersion;pnpDeviceId=$adapters[0].PNPDeviceID;source='Win32_VideoController'}
}
function Run-Player([string]$Mode, [string]$Route, [string]$Id) {
    $observedAdapter = Get-FixtureAdapter
    $observedAdapter | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $runRoot ($Id+'.adapter.json'))
    Invoke-OwnedProcess $player @('-force-d3d12','-screen-fullscreen','0','-screen-width','640','-screen-height','360',
        '-pso-disable-warmup','-max-async-pso-job-count','4','-hotset-observed-driver',$observedAdapter.driverVersion,'-hotset-mode',$Mode,'-hotset-route',$Route,'-hotset-run-id',$Id,
        '-hotset-root',('"'+$runRoot+'"'),'-logFile',('"'+(Join-Path $runRoot ($Id+'.log'))+'"')) 60
}
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceFiles = @('Packages/com.yanagisawa.shader-hitch-pipeline/Core','Packages/com.yanagisawa.shader-hitch-pipeline/Runtime','UnityProject/Assets/PsoHotsetFixture') |
    ForEach-Object { Get-ChildItem -LiteralPath (Join-Path $repoRoot $_) -File -Recurse } |
    Sort-Object FullName | ForEach-Object { @{path=$_.FullName.Substring($repoRoot.Length+1);sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash} }
$binaryFiles = Get-ChildItem -LiteralPath (Split-Path $player) -File -Recurse | Sort-Object FullName |
    ForEach-Object { @{path=$_.FullName.Substring((Split-Path $player).Length+1);sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash} }
$heldOutRoutes = if ($NextHeldOut) { @{ 'next-held-a'=@(0,3,1,2,3); 'next-held-b'=@(0,2,3,1,2) } } else { @{ 'held-a'=@(0,2,1,3,1); 'held-b'=@(0,3,2,3,2) } }
$declaration = [ordered]@{
    schemaVersion=1; workload='four collections of four actual keyword variants; OnWillRenderObject usage events'
    sourceCommit=(& git -C (Join-Path $PSScriptRoot '..') rev-parse HEAD)
    adapter=(Get-FixtureAdapter)
    workingTreeDirty=([bool](& git -C $repoRoot status --porcelain)); sourceFiles=@($sourceFiles); binaryFiles=@($binaryFiles)
    playerSha256=(Get-FileHash $player -Algorithm SHA256).Hash
    requiredUnits=@('u0'); trainingRoutes=@{ 'train-a'=@(0,1,1,2,1); 'train-b'=@(0,1,2,1,1) }
    heldOutRoutes=$heldOutRoutes
    renderPath='explicit-camera-render-backbuffer; real OnWillRenderObject visits required'
    framesPerStage=24; frameTargetHz=120; seed='none: literal immutable route arrays'
    budgetRule='required measured work + 0.5 * sum(optional measured work), frozen before held-out'
    repetitions=$Repetitions; order='route outer, replicate inner, three arms rotated by route+replicate'
    controls=@('required-only','hotset','all-at-once'); driverCache='uncontrolled; never cleared'
    costScope='process-cold calibration after discovery; warm driver cache possible'
    firstPresent='unavailable without OS capture; engine rendered-frame proxy retained'
    workerCount=4; displayedCadence='unavailable: no OS capture in this bounded fixture'
    window='all 119 inter-frame samples across all five stages; no discard; startup engine proxy reported separately'
}
$declaration | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 (Join-Path $runRoot 'declaration.json')
Run-Player 'discover' 'train-a' 'discovery'
Run-Player 'calibrate' 'train-a' 'calibration'
Run-Player 'training' 'train-a' 'training-a'
Run-Player 'training' 'train-b' 'training-b'
$catalogPath = Join-Path $runRoot 'catalog.json'
$catalog = Get-Content -Raw $catalogPath | ConvertFrom-Json
$requiredCost = ($catalog.units | Where-Object requiredStartup | Measure-Object estimatedWarmupMilliseconds -Sum).Sum
$optionalCost = ($catalog.units | Where-Object { !$_.requiredStartup } | Measure-Object estimatedWarmupMilliseconds -Sum).Sum
$training = @('train-a','train-b') | ForEach-Object { Get-Content -Raw (Join-Path $runRoot ($_+'.trace.json')) | ConvertFrom-Json }
@{schemaVersion=1;planSha256=(Get-FileHash (Join-Path $runRoot 'fixture-plan.json') -Algorithm SHA256).Hash.ToLowerInvariant();
    startupBudgetMilliseconds=($requiredCost+0.5*$optionalCost);units=$catalog.units;training=@($training)} |
    ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $runRoot 'frozen-policy.json')
Run-Player 'orchestrator-smoke' 'held-a' 'orchestrator-smoke'
$arms = @('required-only','hotset','all-at-once')
$routes = if ($NextHeldOut) { @('next-held-a','next-held-b') } else { @('held-a','held-b') }
$receipts = @()
for ($routeIndex=0; $routeIndex -lt 2; $routeIndex++) {
    for ($rep=0; $rep -lt $Repetitions; $rep++) {
        for ($position=0; $position -lt 3; $position++) {
            $arm = $arms[($position+$rep+$routeIndex)%3]
            $id = "$($routes[$routeIndex])-$arm-r$rep"
            Run-Player $arm $routes[$routeIndex] $id
            $receipt = Get-Content -Raw (Join-Path $runRoot ($id+'.receipt.json')) | ConvertFrom-Json
            if (!$receipt.completed -or $receipt.coverage.useEvents -le 0) { throw "Incomplete receipt: $id" }
            $receipts += [ordered]@{id=$id;route=$routes[$routeIndex];arm=$arm;replicate=$rep;position=$position;receipt=$receipt}
        }
    }
}
$hashes = Get-ChildItem -LiteralPath $runRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    @{file=$_.FullName.Substring($runRoot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
@{schemaVersion=1;status='bounded-player-evidence';declaration=$declaration;runs=$receipts;hashes=@($hashes);
    missingGates=@('OS first-present capture','opaque driver PSO memory attribution','post-merge integrated acceptance')} |
    ConvertTo-Json -Depth 40 | Set-Content -Encoding utf8 (Join-Path $runRoot 'summary.json')
Write-Output "HOTSET_PLAYER_OK runs=$($receipts.Count) evidence=$runRoot"
