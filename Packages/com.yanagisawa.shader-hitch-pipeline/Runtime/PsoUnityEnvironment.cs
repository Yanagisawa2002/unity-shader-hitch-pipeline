using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoUnityEnvironment
    {
        // Explicit adapter opt-in. All device/Unity/quality/CPU/thread identities are still captured below.
        public static PsoEnvironmentSnapshot CaptureForCostScope(string adapterId, string declaredScope)
        {
            PsoEnvironmentSnapshot environment = Capture();
            environment.costExecutionContext = PsoCostExecutionScope.FromArguments(
                adapterId, declaredScope, System.Environment.GetCommandLineArgs());
            return environment;
        }

        public static PsoEnvironmentSnapshot Capture()
        {
            string quality = "Unknown";
            int qualityIndex = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;
            if (qualityIndex >= 0 && qualityIndex < qualityNames.Length)
                quality = qualityNames[qualityIndex];

            var environment = new PsoEnvironmentSnapshot
            {
                unityVersion = Application.unityVersion,
                engineName = "Unity",
                engineVersion = Application.unityVersion,
                productName = Application.productName,
                applicationVersion = Application.version,
                buildGuid = Application.buildGUID,
                runtimePlatform = Application.platform.ToString(),
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsDeviceVendor = SystemInfo.graphicsDeviceVendor,
                graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion,
                graphicsMemorySizeMb = SystemInfo.graphicsMemorySize,
                qualityLevelName = quality,
                operatingSystem = SystemInfo.operatingSystem,
                graphicsDeviceId = SystemInfo.graphicsDeviceID,
                graphicsDeviceVendorId = SystemInfo.graphicsDeviceVendorID,
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                renderingThreadingMode = SystemInfo.renderingThreadingMode.ToString(),
                // Exact command line is intentionally conservative, including worker/scheduling overrides.
                costExecutionContext = PsoFileUtility.ComputeTextSha256(
                    string.Join("\n", System.Environment.GetCommandLineArgs())),
                identity = PsoUnityBuildIdentity.Capture(),
            };
            PsoWindowsDriverIdentity.Capture(environment);
            return environment;
        }

        public static string CurrentQualityName()
        {
            int qualityIndex = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;
            return qualityIndex >= 0 && qualityIndex < qualityNames.Length
                ? qualityNames[qualityIndex]
                : "Unknown";
        }
    }
}
