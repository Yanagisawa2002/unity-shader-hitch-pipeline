using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Yanagisawa.ShaderHitchPipeline;

// Versioned observation only. This deliberately does not populate the package's
// Windows driver-byte identity or enable reuse of an unvalidated Linux cost cache.
public static class PsoWholeTaskCell
{
    [Serializable] public sealed class Snapshot
    {
        public int version = 1;
        public string contract = "external-runtime-observation-v1";
        public string cell, unityVersion, runtimePlatform, graphicsApi, graphicsDeviceName;
        public string graphicsDeviceVersion, operatingSystem, processorType, renderingThreadingMode;
        public int graphicsDeviceId, graphicsDeviceVendorId, processorCount, graphicsMemorySizeMb, jobWorkerCount;
        public string backend, nativeApiContract, buildGuid, buildInputSha256, shaderSha256, contentSha256;
        public string linuxCgroup, cpuMax, cpusetEffective, memoryMax, nvidiaKernelVersion;
        public string cpuStat, cpuPressure, observationError;
        public bool driverBytesAttested, calibratedLinuxCostReuseSupported;
        public string driverObservationScope = "SystemInfo plus readable proc/cgroup metadata; not loaded-driver byte attestation";
    }

    public static Snapshot Capture()
    {
        var command = PsoCommandLine.Current;
        string cell = command.GetString("-pso-whole-task-cell", "");
        if (string.IsNullOrEmpty(cell)) return null;
        string platform = Application.platform.ToString(), api = SystemInfo.graphicsDeviceType.ToString();
        if (!((cell == "windows-d3d12-v1" && platform == "WindowsPlayer" && api == "Direct3D12") ||
              (cell == "linux-vulkan-v1" && platform == "LinuxPlayer" && api == "Vulkan")))
            throw new InvalidDataException("Declared whole-task cell does not match actual Player platform/API.");
        if (Application.unityVersion != "6000.5.9f1")
            throw new InvalidDataException("New diagnostic cells require the explicitly reviewed 6000.5.9f1 Editor.");
        if (!command.HasFlag("-pso-external-observer-only") || !command.HasFlag(PsoConstants.DisableWarmupArgument))
            throw new InvalidDataException("Initial whole-task cells require the project orchestrator disabled.");
        var identity = PsoUnityBuildIdentity.Capture();
        var issues = PsoCompatibility.ValidateIdentity(identity);
        if (issues.Count != 0) throw new InvalidDataException(string.Join("\n", issues));
        bool native = command.HasFlag("-pso-native-progressive-control");
        var result = new Snapshot {
            cell = cell, unityVersion = Application.unityVersion, runtimePlatform = platform, graphicsApi = api,
            graphicsDeviceName = SystemInfo.graphicsDeviceName, graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion,
            graphicsDeviceId = SystemInfo.graphicsDeviceID, graphicsDeviceVendorId = SystemInfo.graphicsDeviceVendorID,
            graphicsMemorySizeMb = SystemInfo.graphicsMemorySize, operatingSystem = SystemInfo.operatingSystem,
            processorType = SystemInfo.processorType, processorCount = SystemInfo.processorCount,
            jobWorkerCount = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount,
            renderingThreadingMode = SystemInfo.renderingThreadingMode.ToString(), buildGuid = Application.buildGUID,
            buildInputSha256 = identity.buildInputSha256, shaderSha256 = identity.shaderSha256, contentSha256 = identity.contentSha256,
            backend = native ? "unity-native-progressive-control-v1" : "original-route-no-package-warmup-v1",
            nativeApiContract = native ? "UnityEngine.Rendering.GraphicsStateCollection.WarmUpProgressively(int,JobHandle,bool=false)" : "none"
        };
        if (platform == "LinuxPlayer")
        {
            var errors = new List<string>();
            result.linuxCgroup = Read("/proc/self/cgroup", errors);
            // A cgroup namespace root is the only scope this initial Docker adapter
            // recognizes. Do not silently report host-root limits for nested groups.
            if (result.linuxCgroup == "0::/")
            {
                result.cpuMax = Read("/sys/fs/cgroup/cpu.max", errors);
                result.cpusetEffective = Read("/sys/fs/cgroup/cpuset.cpus.effective", errors);
                result.memoryMax = Read("/sys/fs/cgroup/memory.max", errors);
                result.cpuStat = Read("/sys/fs/cgroup/cpu.stat", errors);
                result.cpuPressure = Read("/sys/fs/cgroup/cpu.pressure", errors);
            }
            else errors.Add("Non-root/missing cgroup v2 namespace; effective container limits unavailable.");
            result.nvidiaKernelVersion = Read("/proc/driver/nvidia/version", errors);
            result.observationError = string.Join("\n", errors);
        }
        return result;
    }

    static string Read(string path, List<string> errors)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (Exception error) { errors.Add(path + ": " + error.GetType().Name); return null; }
    }
}
