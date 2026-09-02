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

        public int schemaVersion = PsoConstants.SchemaVersion;
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
        public double startupDeadlineMilliseconds = 1000.0;
        public double deferredDeadlineMilliseconds;
        public double estimatedMillisecondsPerState = 0.25;
        public double startupExpectedUseProbability = 1.0;
        public double deferredExpectedUseProbability = 0.5;
        public int startupHotSetTier;
        public int deferredHotSetTier = 1;

        public static PsoProjectConfiguration Load()
        {
            if (!File.Exists(ConfigurationPath))
                return new PsoProjectConfiguration();

            PsoProjectConfiguration result = JsonUtility.FromJson<PsoProjectConfiguration>(
                File.ReadAllText(ConfigurationPath));
            if (result == null)
                return new PsoProjectConfiguration();
            if (result.schemaVersion < 2)
            {
                result.schemaVersion = PsoConstants.SchemaVersion;
                result.startupDeadlineMilliseconds = 1000.0;
                result.deferredDeadlineMilliseconds = 0.0;
                result.estimatedMillisecondsPerState = 0.25;
                result.startupExpectedUseProbability = 1.0;
                result.deferredExpectedUseProbability = 0.5;
                result.startupHotSetTier = 0;
                result.deferredHotSetTier = 1;
            }
            return result;
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
            if (schemaVersion != PsoConstants.SchemaVersion)
                throw new InvalidOperationException(
                    "Unsupported configuration schemaVersion: " + schemaVersion + ".");
            if (string.IsNullOrWhiteSpace(profileId))
                throw new InvalidOperationException("profileId is required.");
            if (minimumBatchSize < 1 || maximumBatchSize < minimumBatchSize)
                throw new InvalidOperationException("Warmup batch bounds are invalid.");
            if (initialBatchSize < minimumBatchSize || initialBatchSize > maximumBatchSize)
                throw new InvalidOperationException("initialBatchSize is outside its bounds.");
            if (targetFrameMilliseconds <= 0.0)
                throw new InvalidOperationException("targetFrameMilliseconds must be positive.");
            if (startupDeadlineMilliseconds < 0.0 || deferredDeadlineMilliseconds < 0.0)
                throw new InvalidOperationException("Warmup deadlines cannot be negative.");
            if (estimatedMillisecondsPerState <= 0.0)
                throw new InvalidOperationException(
                    "estimatedMillisecondsPerState must be positive.");
            if (startupExpectedUseProbability < 0.0 ||
                startupExpectedUseProbability > 1.0 ||
                deferredExpectedUseProbability < 0.0 ||
                deferredExpectedUseProbability > 1.0)
                throw new InvalidOperationException(
                    "Expected-use probabilities must be between zero and one.");
            if (startupHotSetTier < 0 || deferredHotSetTier < 0)
                throw new InvalidOperationException("Hot-set tiers cannot be negative.");
        }
    }
}
