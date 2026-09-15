#requires -Version 7.0
$ErrorActionPreference='Stop'
$shaderRoot='D:\CodexWork\shader-whole-task-20260915'
$shaderLeasePath='D:\CodexWork\whole-task-validation-20260915\coordination\local-preparation-lease.json'
$shaderLease=Get-Content -LiteralPath $shaderLeasePath -Raw | ConvertFrom-Json
$shaderLeaseHash=(Get-FileHash -LiteralPath $shaderLeasePath).Hash
$shaderDeadline=([DateTimeOffset]$shaderLease.expiresAtUtc).UtcDateTime
if($shaderLease.generation -ne 2 -or $shaderLease.owner -ne 'shader' -or
   $shaderLease.ownerThreadId -ne '01a0a3de-7f19-7be2-b268-b478036a9263' -or
   $shaderLease.status -ne 'granted' -or $shaderLease.scope -ne 'pinned-nsisbi-private-module-extraction-only' -or
   $shaderLease.growthEnvelopeGiB -ne 1 -or $shaderLease.decodeProcessMaxMiB -ne 512 -or
   $shaderLease.copyWorkersMax -ne 1 -or $shaderLease.maxElapsedMinutes -ne 15 -or
   $shaderLease.freeDataReserveGiB -ne 20 -or $shaderLease.minFreeMemoryGiB -ne 8 -or
   $shaderLease.mutex -ne 'Local\CodexR9700VNextUnityGpu') {throw 'Generation2 grant mismatch'}
$shaderOutput=Join-Path $shaderRoot 'work\whole-task-20260915\private-module-extraction-g2'
$shaderTarget=Join-Path $shaderRoot 'work\toolchains\unity-6000.5.9f1\Editor\Data\PlaybackEngines\LinuxStandaloneSupport'
if([IO.Path]::GetFullPath($shaderLease.stagingRoot) -ne $shaderOutput) {throw 'Wrong staging root'}
if([DateTime]::UtcNow -ge $shaderDeadline.AddSeconds(-10)) {throw 'Grant expired before setup'}
$shaderPython='C:\Users\cgliu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$shaderCode=Join-Path $shaderRoot 'Tools\prepare_pso_linux_module.py'
$shaderSelfTest=& $shaderPython -B $shaderCode --self-test
if($LASTEXITCODE -ne 0) {throw 'Offline rejection checks failed'}
$shaderTemp=Join-Path $PSScriptRoot 'supervisor-temp-generation2'
if(Test-Path -LiteralPath $shaderTemp) {throw 'Retain previous temporary evidence'}
New-Item -ItemType Directory -Path $shaderTemp | Out-Null
$shaderOld=@{}
foreach($name in @('TEMP','TMP','PYTHONDONTWRITEBYTECODE','UPM_CACHE_ROOT','NUGET_PACKAGES','DOTNET_CLI_HOME')) {
    $shaderOld[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
    $value=if($name -eq 'PYTHONDONTWRITEBYTECODE') {'1'} else {Join-Path $shaderTemp $name}
    if($name -ne 'PYTHONDONTWRITEBYTECODE') {New-Item -ItemType Directory -Path $value | Out-Null}
    [Environment]::SetEnvironmentVariable($name,$value,'Process')
}
$shaderOutcome='failed'
try {
    & (Join-Path $PSScriptRoot 'Invoke-Generation2FileStage.ps1') -Output $shaderOutput -BudgetPath $shaderTarget -EstimatedAdditionalPeakGiB 1 -Action {
        if((Get-FileHash -LiteralPath $shaderLeasePath).Hash -ne $shaderLeaseHash) {throw 'Lease changed before action'}
        if([DateTime]::UtcNow -ge $shaderDeadline.AddSeconds(-10)) {throw 'Absolute grant deadline'}
        $freeMemory=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory*1KB
        if($freeMemory -lt 8GB) {throw 'Less than 8 GiB free memory'}
        $shaderSelfTest | Set-Content -LiteralPath (Join-Path $shaderOutput 'offline-rejections.json') -Encoding utf8
        $installer=Join-Path $shaderRoot 'work\toolchains\unity-6000.5.9f1\downloads\UnitySetup-Linux-IL2CPP-Support-for-Editor-6000.5.9f1.exe'
        $signature=Get-AuthenticodeSignature -LiteralPath $installer
        [ordered]@{utc=[DateTime]::UtcNow.ToString('o');status=$signature.Status.ToString();signer=$signature.SignerCertificate.Subject;thumbprint=$signature.SignerCertificate.Thumbprint;installerExecuted=$false} |
          ConvertTo-Json | Set-Content -LiteralPath (Join-Path $shaderOutput 'authenticode.json') -Encoding utf8
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne '228FB6411B0A144478C86AAA3CD9473C43A8ABA7') {throw 'Pinned Unity signer not valid'}
        [ordered]@{utc=[DateTime]::UtcNow.ToString('o');deadlineUtc=$shaderDeadline.ToString('o');sourceLeaseSha256=$shaderLeaseHash.ToLowerInvariant();freeMemoryBytes=$freeMemory;freeDBytes=([IO.DriveInfo]::new('D:\')).AvailableFreeSpace;processTemp=[IO.Path]::GetTempPath();extractorSha256=(Get-FileHash -LiteralPath $shaderCode).Hash.ToLowerInvariant();sourceHead=(git -C $shaderRoot rev-parse HEAD)} |
          ConvertTo-Json | Set-Content -LiteralPath (Join-Path $shaderOutput 'preflight.json') -Encoding utf8
        $start=[Diagnostics.ProcessStartInfo]::new()
        $start.FileName=$shaderPython
        $start.UseShellExecute=$false
        $start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true
        $start.RedirectStandardError=$true
        $start.WorkingDirectory=$shaderRoot
        foreach($arg in @('-B',$shaderCode,'--run-granted-generation2')) {$start.ArgumentList.Add($arg)}
        $child=[Diagnostics.Process]::Start($start)
        $stdout=$child.StandardOutput.ReadToEndAsync()
        $stderr=$child.StandardError.ReadToEndAsync()
        $birth=$child.StartTime.ToUniversalTime()
        $process=[ordered]@{pid=$child.Id;startedUtc=$birth.ToString('o');startTicks=$birth.Ticks;executable=$start.FileName;arguments=@($start.ArgumentList);exitCode=$null;stopReason=$null;allOwnedExited=$false}
        $process | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $shaderOutput 'process-active.json') -Encoding utf8
        try {
            do {
                $done=$child.WaitForExit(1000)
                $freeMemory=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory*1KB
                $freeD=([IO.DriveInfo]::new('D:\')).AvailableFreeSpace
                $bytes=[long](Get-ChildItem -LiteralPath $shaderOutput,$shaderTemp -File -Recurse | Measure-Object Length -Sum).Sum
                if(Test-Path -LiteralPath $shaderTarget) {$bytes += [long](Get-ChildItem -LiteralPath $shaderTarget -File -Recurse | Measure-Object Length -Sum).Sum}
                $privateBytes=$null
                if(-not $done) {$child.Refresh();$privateBytes=$child.PrivateMemorySize64}
                [ordered]@{utc=[DateTime]::UtcNow.ToString('o');freeMemoryBytes=$freeMemory;freeDBytes=$freeD;newLogicalBytes=$bytes;processPrivateBytes=$privateBytes;processExited=$done} |
                  ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $shaderOutput 'resource-samples.jsonl') -Encoding utf8
                if([DateTime]::UtcNow -ge $shaderDeadline.AddSeconds(-2)) {$process.stopReason='Absolute grant deadline'}
                elseif($freeMemory -lt 8GB) {$process.stopReason='Free memory below 8 GiB'}
                elseif($freeD -lt 20GB) {$process.stopReason='D reserve below 20 GiB'}
                elseif($bytes -ge 1GB) {$process.stopReason='New logical data at 1 GiB limit'}
                elseif($privateBytes -gt 512MB) {$process.stopReason='Private committed memory over 512 MiB'}
                elseif((Get-FileHash -LiteralPath $shaderLeasePath).Hash -ne $shaderLeaseHash) {$process.stopReason='Lease revoked or changed'}
                if($process.stopReason -and -not $done) {$child.Kill();$child.WaitForExit();$done=$true}
            } while(-not $done)
        } finally {
            if(-not $child.HasExited) {$process.stopReason='Supervisor exception';$child.Kill();$child.WaitForExit()}
            $process.exitCode=$child.ExitCode
            $process.finishedUtc=[DateTime]::UtcNow.ToString('o')
            $process.allOwnedExited=$child.HasExited
            [IO.File]::WriteAllText((Join-Path $shaderOutput 'worker.stdout.log'),$stdout.GetAwaiter().GetResult())
            [IO.File]::WriteAllText((Join-Path $shaderOutput 'worker.stderr.log'),$stderr.GetAwaiter().GetResult())
            $child.Dispose()
            $process | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $shaderOutput 'process.json') -Encoding utf8
        }
        if($process.stopReason) {throw $process.stopReason}
        if($process.exitCode -ne 0) {throw "Generation2 worker exit $($process.exitCode)"}
        $global:LASTEXITCODE=0
    }
    $shaderOutcome='completed'
} catch {
    Write-Output ('GENERATION2_FAILED: '+$_.Exception.Message)
} finally {
    foreach($name in $shaderOld.Keys) {[Environment]::SetEnvironmentVariable($name,$shaderOld[$name],'Process')}
    Write-Output ("GENERATION2 outcome={0} evidence={1}" -f $shaderOutcome,$shaderOutput)
}
if($shaderOutcome -ne 'completed') {exit 1}
