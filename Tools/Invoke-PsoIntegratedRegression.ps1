[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ValidationLockRunner,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.5.2f1/Editor/Unity.exe',
    [string]$Python = 'python'
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Use a new evidence directory; refusing to overwrite $outputRoot."
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$receipt = [ordered]@{
    schemaVersion = 1
    kind = 'integrated-correctness-regression'
    startedUtc = [DateTime]::UtcNow.ToString('o')
    sourceSha = (& git -C $repo rev-parse HEAD).Trim()
    sourceStatus = @(& git -C $repo status --porcelain)
    unity = $Unity
    unitySha256 = (Get-FileHash -LiteralPath $Unity -Algorithm SHA256).Hash.ToLowerInvariant()
    result = 'running'
    checks = @()
    error = $null
}

try {
    # One shared lock covers every build and Unity child through process exit.
    # The caller must not already hold the same mutex.
    & $ValidationLockRunner -Action {
        Push-Location $repo
        try {
            & $Python -m unittest discover -s Tools/tests -v 2>&1 |
                Tee-Object -FilePath (Join-Path $outputRoot 'python-tests.log')
            if ($LASTEXITCODE -ne 0) { throw 'Python regression failed.' }
            $receipt.checks += @{ name = 'python-unittest'; result = 'passed'; log = 'python-tests.log' }

            & dotnet run --disable-build-servers --property:UseSharedCompilation=false --project DotNet/ShaderHitchPipeline.Core.Smoke/ShaderHitchPipeline.Core.Smoke.csproj 2>&1 |
                Tee-Object -FilePath (Join-Path $outputRoot 'core-smoke.log')
            if ($LASTEXITCODE -ne 0) { throw 'Engine-neutral core smoke failed.' }
            $receipt.checks += @{ name = 'core-smoke'; result = 'passed'; log = 'core-smoke.log' }

            $results = Join-Path $outputRoot 'editmode.xml'
            $log = Join-Path $outputRoot 'unity-editmode.log'
            $unityArguments = @(
                '-batchmode', '-nographics', '-projectPath', ('"' + (Join-Path $repo 'UnityProject') + '"'),
                '-runTests', '-testPlatform', 'EditMode',
                '-testResults', ('"' + $results + '"'), '-logFile', ('"' + $log + '"')
            )
            $child = Start-Process -FilePath $Unity -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
            $child.WaitForExit()
            if ($child.ExitCode -ne 0) { throw "Unity test process exited $($child.ExitCode)." }
            if (-not (Test-Path -LiteralPath $results)) { throw 'Unity produced no test result XML.' }
            [xml]$xml = Get-Content -Raw -LiteralPath $results
            $run = $xml.'test-run'
            if ($null -eq $run -or [int]$run.total -le 0 -or [int]$run.failed -ne 0 -or $run.result -ne 'Passed') {
                throw 'Unity regression did not pass; inspect editmode.xml.'
            }
            $receipt.checks += @{
                name = 'unity-editmode'; result = $run.result
                total = [int]$run.total; passed = [int]$run.passed
                failed = [int]$run.failed; skipped = [int]$run.skipped
                log = 'unity-editmode.log'; results = 'editmode.xml'
            }
        } finally { Pop-Location }
    }
    $receipt.result = 'passed'
} catch {
    $receipt.result = 'failed'
    $receipt.error = $_.Exception.Message
    throw
} finally {
    $receipt.endedUtc = [DateTime]::UtcNow.ToString('o')
    $receipt.artifacts = @(Get-ChildItem -LiteralPath $outputRoot -File | ForEach-Object {
        @{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot 'regression.json') -Encoding utf8
}
$receipt | ConvertTo-Json -Depth 8
