[CmdletBinding()]
param(
    [string]$Python = 'python',
    [string]$UnityManagedPath = '',
    [string]$ExpectedUnityVersion = '',
    [ValidateSet('', 'UNITY_6000_5_OR_NEWER')][string]$UnityApiDefines = '',
    [string]$EntitiesAssembliesPath = ''
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Get-Command dotnet -ErrorAction Stop | Out-Null
Get-Command $Python -ErrorAction Stop | Out-Null
Push-Location $repo
try {
    # Explicit allowlist: never discover future tests that may start an engine/benchmark.
    & (Join-Path $PSScriptRoot 'tests/Test-PsoProcessOwnership.ps1')
    & $Python Tools/run_cpu_validation.py
    if ($LASTEXITCODE -ne 0) { throw 'Pure CPU Python validation failed.' }
    foreach ($project in @('ShaderHitchPipeline.Core.Smoke', 'ShaderHitchPipeline.Streaming.Smoke', 'ShaderHitchPipeline.Scheduler.Tests', 'ShaderHitchPipeline.PolicyExample', 'ShaderHitchPipeline.WindowsModules.Smoke')) {
        $testOutput = @(& dotnet run --disable-build-servers --property:UseSharedCompilation=false --project "DotNet/$project/$project.csproj" --configuration Release)
        $testExitCode = $LASTEXITCODE
        $testOutput | Write-Output
        if ($testExitCode -ne 0) { throw "Pure CPU validation failed: $project" }
        if ($project -eq 'ShaderHitchPipeline.Scheduler.Tests') {
            $fixturePrefix = 'SCHEDULER_FEEDBACK_FIXTURE '
            $fixtureLines = @($testOutput | Where-Object { $_.StartsWith($fixturePrefix) })
            if ($fixtureLines.Count -ne 1) { throw 'Missing or ambiguous mock scheduling feedback fixture.' }
            $fixturePath = Join-Path ([IO.Path]::GetTempPath()) ('pso-scheduler-' + [Guid]::NewGuid().ToString('N') + '.json')
            try {
                [IO.File]::WriteAllText($fixturePath, $fixtureLines[0].Substring($fixturePrefix.Length), [Text.UTF8Encoding]::new($false))
                & $Python Tools/validate_pso_documents.py --scheduling $fixturePath
                if ($LASTEXITCODE -ne 0) { throw 'Actual mock scheduler serialization differs from the feedback schema.' }
            } finally {
                if (Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath }
            }
        }
    }
    if ($UnityManagedPath) {
        $managed = [IO.Path]::GetFullPath($UnityManagedPath)
        if (-not (Test-Path -LiteralPath (Join-Path $managed 'UnityEngine/UnityEngine.CoreModule.dll'))) {
            throw 'UnityManagedPath must name Editor/Data/Managed of the pinned editor.'
        }
        # Read PE metadata; never invoke the Editor to discover its version.
        $editor = Join-Path (Split-Path -Parent (Split-Path -Parent $managed)) 'Unity.exe'
        if (Test-Path -LiteralPath $editor) {
            $version = ([Diagnostics.FileVersionInfo]::GetVersionInfo($editor).ProductVersion -split '_')[0]
            if ($ExpectedUnityVersion -and $version -ne $ExpectedUnityVersion) { throw "Expected Unity $ExpectedUnityVersion, found $version." }
            if ($version -notmatch '^6000\.(\d+)\.') { throw "Unsupported Unity reference version: $version" }
            $promotedApi = [int]$Matches[1] -ge 5
            if ($promotedApi -ne ($UnityApiDefines -eq 'UNITY_6000_5_OR_NEWER')) {
                throw 'Unity API namespace define does not match the supplied reference assemblies.'
            }
        } elseif ($ExpectedUnityVersion) { throw 'Cannot verify the expected editor version from reference metadata.' }
        if ($IsWindows -and $repo.Length -gt 160) {
            throw 'Use a short checkout such as C:/src/pso for Unity reference compilation; this root leaves insufficient path headroom.'
        }
        & dotnet build DotNet/ShaderHitchPipeline.Unity.Compile/ShaderHitchPipeline.Unity.Compile.csproj --configuration Release --nologo --disable-build-servers --property:UseSharedCompilation=false "-p:UnityManagedPath=$managed" "-p:UnityApiDefines=$UnityApiDefines" "-p:EntitiesAssembliesPath=$EntitiesAssembliesPath"
        if ($LASTEXITCODE -ne 0) { throw 'Unity API reference compilation failed.' }
    }
    Write-Host 'PSO_VALIDATION_OK measurementStatus=Unmeasured; no Unity/Player/GPU execution'
} finally { Pop-Location }
