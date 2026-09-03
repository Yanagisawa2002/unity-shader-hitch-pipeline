using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoUnityEnvironment
    {
        public static PsoEnvironmentSnapshot Capture()
        {
            string quality = "Unknown";
            int qualityIndex = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;
            if (qualityIndex >= 0 && qualityIndex < qualityNames.Length)
                quality = qualityNames[qualityIndex];

            return new PsoEnvironmentSnapshot
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
            };
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
