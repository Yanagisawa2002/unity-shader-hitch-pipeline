using System;
using System.IO;
using System.Linq;
using Yanagisawa.ShaderHitchPipeline;

// Read-only calls to the actual production module provider. No Unity process,
// graphics API, warmup, clock, synthetic timings or driver changes are involved.
if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("PSO_WINDOWS_MODULES_SKIPPED platform=non-Windows measurementStatus=Unmeasured");
    return;
}

for (int capture = 0; capture < 2; capture++)
{
    string[] paths = PsoWindowsProcessModules.CaptureFileNames();
    if (paths.Length < 2 || paths.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)))
        throw new InvalidOperationException("Actual module inventory is empty, relative or points to a missing file.");
    if (!paths.Any(path => string.Equals(Path.GetFileName(path), "kernel32.dll", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("The actual inventory omits kernel32.dll.");
    if (Environment.ProcessPath is not string processPath || !paths.Contains(processPath, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException("The actual inventory omits the current executable.");
}
Console.WriteLine("PSO_WINDOWS_MODULES_OK captures=2 source=actual-Win32-current-process-api measurementStatus=Unmeasured");
