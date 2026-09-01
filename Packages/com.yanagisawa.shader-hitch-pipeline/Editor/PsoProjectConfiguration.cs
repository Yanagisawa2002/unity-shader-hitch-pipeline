using System;
using System.IO;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline.Editor
{
    [Serializable]
    public sealed class PsoProjectConfiguration
    {
        public const string ConfigurationPath =
            "ProjectSettings/ShaderHitchPipeline.json";

        public string inboxDirectory = "PsoArtifacts/Inbox";
        public string profileOutputDirectory = "PsoArtifacts/Profiles";
        public string installedPlanDirectory =
            "Assets/StreamingAssets/ShaderHitchPipeline";
        public string profileId = "windows-player-default";
        public string targetRuntimePlatform = "WindowsPlayer";
        public string targetGraphicsDeviceType = "Direct3D12";
        public string targetQualityLevelName = string.Empty;
        public bool validateInstalledPlanBeforeBuild = true;
        public int initialBatchSize = 8;
        public int minimumBatchSize = 1;
        public int maximumBatchSize = 64;
        public double targetFrameMilliseconds = 16.67;

        public static PsoProjectConfiguration Load()
        {
            if (!File.Exists(ConfigurationPath))
                return new PsoProjectConfiguration();

            PsoProjectConfiguration result = JsonUtility.FromJson<PsoProjectConfiguration>(
                File.ReadAllText(ConfigurationPath));
            return result ?? new PsoProjectConfiguration();
        }

        public void Save()
        {
            PsoFileUtility.WriteJsonAtomic(ConfigurationPath, this);
        }

        public string ResolveProjectPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("A configured path is empty.");
            return Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(Directory.GetCurrentDirectory(), value));
        }

        public void ValidateSettings()
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new InvalidOperationException("profileId is required.");
            if (minimumBatchSize < 1 || maximumBatchSize < minimumBatchSize)
                throw new InvalidOperationException("Warmup batch bounds are invalid.");
            if (initialBatchSize < minimumBatchSize || initialBatchSize > maximumBatchSize)
                throw new InvalidOperationException("initialBatchSize is outside its bounds.");
            if (targetFrameMilliseconds <= 0.0)
                throw new InvalidOperationException("targetFrameMilliseconds must be positive.");
        }
    }
}
