#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HostPath,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$Method,
    [string]$PlayerPath = '',
    [switch]$Training,
    [string[]]$ExtraArguments = @(),
    [double]$EstimatedAdditionalPeakGiB = 60,
    [int]$MaximumSeconds = 14400
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$nativeHost = [IO.Path]::GetFullPath($HostPath)
$stage = [IO.Path]::GetFullPath($Output)
$relativeHost = [IO.Path]::GetRelativePath($repo, $nativeHost)
if ($relativeHost.StartsWith('..') -or [IO.Path]::IsPathRooted($relativeHost)) { throw 'The external host must be within this repository work directory.' }
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.1.0f1/Editor/Unity.exe'
& (Join-Path $PSScriptRoot 'Invoke-PsoNativeStage.ps1') -Output $stage -BudgetPath $nativeHost -EstimatedAdditionalPeakGiB $EstimatedAdditionalPeakGiB -Action {
    $mapped = $false
    try {
        if (Test-Path N:/) { throw 'N: is occupied. Leave the existing mapping untouched.' }
        & subst N: $repo
        if ($LASTEXITCODE -ne 0) { throw 'Short-path mapping failed.' }
        $mapped = $true
        $ownedMapping = @(& subst) | Where-Object { $_.StartsWith('N:\: => ') }
        if (@($ownedMapping).Count -ne 1) { throw 'Could not record the owned short-path mapping.' }
        if ((Get-Content -Raw N:/.git) -ne (Get-Content -Raw (Join-Path $repo '.git'))) { throw 'Short-path mapping identity mismatch.' }
        $shortHost = Join-Path 'N:/' $relativeHost
        $arguments = @('-batchmode', '-quit', '-buildTarget', 'Win64', '-force-d3d12', '-pso-build-input-evidence',
            '-projectPath', ('"' + $shortHost + '"'), '-executeMethod', $Method,
            '-logFile', ('"' + (Join-Path $stage 'editor.log') + '"'))
        if ($Training) { $arguments += '-pso-training-build' }
        if ($PlayerPath) {
            $playerFull = [IO.Path]::GetFullPath($PlayerPath)
            $relativePlayer = [IO.Path]::GetRelativePath($repo, $playerFull)
            if ($relativePlayer.StartsWith('..')) { throw 'Player output must be within this repository work directory.' }
            $arguments += @('-pso-native-build-output', ('"' + (Join-Path 'N:/' $relativePlayer) + '"'),
                '-pso-build-receipt', ('"' + (Join-Path $stage 'build-summary.json') + '"'))
        }
        $arguments += $ExtraArguments
        foreach ($suffix in @('before')) {
            New-Item -ItemType Directory -Path (Join-Path $stage "settings-$suffix") | Out-Null
            Copy-Item -LiteralPath (Join-Path $nativeHost 'ProjectSettings') -Destination (Join-Path $stage "settings-$suffix") -Recurse
            Copy-Item -LiteralPath (Join-Path $nativeHost 'Packages/manifest.json') -Destination (Join-Path $stage "manifest-$suffix.json")
            if (Test-Path (Join-Path $nativeHost 'Packages/packages-lock.json')) {
                Copy-Item -LiteralPath (Join-Path $nativeHost 'Packages/packages-lock.json') -Destination (Join-Path $stage "packages-lock-$suffix.json")
            }
        }
        [ordered]@{ startedUtc = [DateTime]::UtcNow.ToString('o'); unity = $unity;
            unitySha256 = (Get-FileHash -LiteralPath $unity).Hash.ToLowerInvariant(); arguments = $arguments;
            integrationCommit = (& git -C $repo rev-parse HEAD).Trim(); sourceDiff = @(& git -C $repo status --short);
            hostPath = $nativeHost; shortHost = $shortHost; training = [bool]$Training } |
            ConvertTo-Json -Depth 6 | Set-Content (Join-Path $stage 'command.json') -Encoding utf8
        & git -C $repo diff --binary "--output=$stage/integration.diff"
        $inputs = Join-Path $stage 'source-inputs'
        New-Item -ItemType Directory -Path $inputs | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo 'Packages/com.yanagisawa.shader-hitch-pipeline') -Destination $inputs -Recurse
        $hostManifest = Get-Content -Raw -LiteralPath (Join-Path $nativeHost 'Packages/manifest.json') | ConvertFrom-Json
        if ($hostManifest.dependencies.PSObject.Properties.Name -contains 'com.yanagisawa.shader-hitch-native-scenes') {
            $nativePackage = Join-Path $repo 'Integrations/MegacityMetroNative/Package'
            $declared = $hostManifest.dependencies.'com.yanagisawa.shader-hitch-native-scenes'
            if (-not $declared.StartsWith('file:') -or
                [IO.Path]::GetFullPath($declared.Substring(5), (Join-Path $nativeHost 'Packages')) -ne $nativePackage) {
                throw 'Unexpected native observer package source; record and review it before building.'
            }
            Copy-Item -LiteralPath $nativePackage -Destination (Join-Path $inputs 'com.yanagisawa.shader-hitch-native-scenes') -Recurse
        }
        Get-ChildItem -LiteralPath (Join-Path $nativeHost 'Assets') -Directory -Filter 'Pso*Adapter' | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $inputs -Recurse
        }
        $child = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
        & (Join-Path $PSScriptRoot 'Wait-PsoOwnedProcess.ps1') -Process $child -EvidenceDirectory $stage -BudgetPath $nativeHost -LogPath (Join-Path $stage 'editor.log') -MaximumSeconds $MaximumSeconds
    } catch {
        @{ utc = [DateTime]::UtcNow.ToString('o'); error = $_.Exception.Message } |
            ConvertTo-Json | Set-Content (Join-Path $stage 'stage-failure.json') -Encoding utf8
        throw
    } finally {
      try {
        $after = Join-Path $stage 'settings-after'
        New-Item -ItemType Directory -Path $after | Out-Null
        Copy-Item -LiteralPath (Join-Path $nativeHost 'ProjectSettings') -Destination $after -Recurse
        Copy-Item -LiteralPath (Join-Path $nativeHost 'Packages/manifest.json') -Destination (Join-Path $stage 'manifest-after.json')
        foreach ($relative in @('PsoArtifacts/BuildInputEvidence', 'Assets/Resources/PsoBuildIdentity.json')) {
            $evidenceSource = Join-Path $nativeHost $relative
            if (Test-Path -LiteralPath $evidenceSource) { Copy-Item -LiteralPath $evidenceSource -Destination $stage -Recurse }
        }
        if (Test-Path (Join-Path $nativeHost 'Packages/packages-lock.json')) {
            Copy-Item -LiteralPath (Join-Path $nativeHost 'Packages/packages-lock.json') -Destination (Join-Path $stage 'packages-lock-after.json')
        }
        if (Test-Path (Join-Path $nativeHost '.git')) {
            & git -C $nativeHost diff --binary "--output=$stage/host.diff"
            & git -C $nativeHost status --porcelain=v1 --untracked-files=all | Set-Content (Join-Path $stage 'host-status.txt') -Encoding utf8
        }
      } catch {
        @{ utc = [DateTime]::UtcNow.ToString('o'); error = $_.Exception.Message } |
            ConvertTo-Json | Set-Content (Join-Path $stage 'evidence-failure.json') -Encoding utf8
        throw
      } finally {
        if ($mapped) {
            $currentMapping = @(& subst) | Where-Object { $_.StartsWith('N:\: => ') }
            if ($currentMapping -cne $ownedMapping) { throw 'N: ownership changed; leave the new mapping untouched.' }
            & subst N: /D
            if ($LASTEXITCODE -ne 0) { throw 'Could not release our short-path mapping.' }
        }
        @{ finishedUtc = [DateTime]::UtcNow.ToString('o'); shortPathReleased = -not (Test-Path N:/) } |
            ConvertTo-Json | Set-Content (Join-Path $stage 'short-path-release.json') -Encoding utf8
      }
    }
}
