using System;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    [Serializable]
    public sealed class PsoEnvironmentSnapshot
    {
        public string unityVersion;
        public string productName;
        public string applicationVersion;
        public string buildGuid;
        public string runtimePlatform;
        public string graphicsDeviceType;
        public string graphicsDeviceName;
        public string graphicsDeviceVendor;
        public string graphicsDeviceVersion;
        public int graphicsMemorySizeMb;
        public string qualityLevelName;

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
            };
        }
    }

    [Serializable]
    public sealed class PsoSessionManifest
    {
        public int schemaVersion = PsoConstants.SchemaVersion;
        public string packageVersion = PsoConstants.PackageVersion;
        public string sessionId;
        public string phase;
        public string startedUtc;
        public string endedUtc;
        public string collectionFile;
        public string collectionSha256;
        public int variantCount;
        public int graphicsStateCount;
        public bool saved;
        public bool sentToEditor;
        public string error;
        public string[] tags = Array.Empty<string>();
        public PsoEnvironmentSnapshot environment;
    }

    [Serializable]
    public sealed class PsoWarmupPhasePlan
    {
        public string phase;
        public string collectionFile;
        public string collectionSha256;
        public int variantCount;
        public int graphicsStateCount;
        public bool required = true;
        public bool prewarmAtStartup = true;
        public bool traceCacheMisses = true;
        public int priority;
        public int initialBatchSize = 8;
        public int minimumBatchSize = 1;
        public int maximumBatchSize = 64;
        public double targetFrameMilliseconds = 16.67;
        public double deadlineMilliseconds;
        public double estimatedMillisecondsPerState = 0.25;
        public double expectedUseProbability = 1.0;
        public int hotSetTier = 1;
    }

    [Serializable]
    public sealed class PsoWarmupPlanDocument
    {
        public int schemaVersion = PsoConstants.SchemaVersion;
        public string packageVersion = PsoConstants.PackageVersion;
        public string profileId;
        public string generatedUtc;
        public string runtimePlatform;
        public string graphicsDeviceType;
        public string qualityLevelName;
        public int sourceSessionCount;
        public string[] sourceSessionHashes = Array.Empty<string>();
        public PsoWarmupPhasePlan[] phases = Array.Empty<PsoWarmupPhasePlan>();
        public string planSha256;
    }

    [Serializable]
    public sealed class PsoMergeInputReceipt
    {
        public string manifestFile;
        public string collectionFile;
        public string collectionSha256;
        public string sessionId;
        public string phase;
        public int statesBefore;
        public int statesAfter;
        public int statesAdded;
        public bool accepted;
        public string reason;
    }

    [Serializable]
    public sealed class PsoMergeReceipt
    {
        public int schemaVersion = PsoConstants.SchemaVersion;
        public string packageVersion = PsoConstants.PackageVersion;
        public string generatedUtc;
        public string profileId;
        public string outputPlan;
        public string outputPlanSha256;
        public int acceptedSessions;
        public int rejectedSessions;
        public PsoMergeInputReceipt[] inputs = Array.Empty<PsoMergeInputReceipt>();
    }

    [Serializable]
    public sealed class PsoFrameStatistics
    {
        public int sampleCount;
        public double minimumMilliseconds;
        public double meanMilliseconds;
        public double p50Milliseconds;
        public double p95Milliseconds;
        public double p99Milliseconds;
        public double maximumMilliseconds;
        public double standardDeviationMilliseconds;
        public double hitchThresholdMilliseconds;
        public int hitchFrameCount;
        public double hitchFramePercent;
    }

    [Serializable]
    public sealed class PsoMarkerStatistics
    {
        public string markerName;
        public bool available;
        public long sampleCount;
        public double totalMilliseconds;
        public double maximumFrameMilliseconds;
    }

    [Serializable]
    public sealed class PsoBenchmarkReceipt
    {
        public int schemaVersion = PsoConstants.SchemaVersion;
        public string packageVersion = PsoConstants.PackageVersion;
        public string runId;
        public string mode;
        public string startedUtc;
        public string endedUtc;
        public string planFile;
        public string planSha256;
        public int discardFrames;
        public int requestedSampleFrames;
        public bool completed;
        public string error;
        public PsoEnvironmentSnapshot environment;
        public PsoFrameStatistics frameTimes;
        public double[] frameTimeSamplesMilliseconds = Array.Empty<double>();
        public PsoMarkerStatistics[] profilerMarkers = Array.Empty<PsoMarkerStatistics>();
    }

    [Serializable]
    public sealed class PsoBuildReceipt
    {
        public int schemaVersion = PsoConstants.SchemaVersion;
        public string packageVersion = PsoConstants.PackageVersion;
        public string buildGuid;
        public string generatedUtc;
        public string buildTarget;
        public string outputPath;
        public string result;
        public string planFile;
        public string planSha256;
        public string runtimePlatform;
        public string graphicsDeviceType;
        public string[] enabledGraphicsApis = Array.Empty<string>();
    }

    [Serializable]
    public sealed class PsoWarmupPhaseReceipt
    {
        public string phase;
        public string collectionFile;
        public string strategy;
        public int hotSetTier;
        public double deadlineMilliseconds;
        public double expectedUseProbability;
        public int totalGraphicsStates;
        public int completedGraphicsStates;
        public int initialBatchSize;
        public int finalBatchSize;
        public int batchCount;
        public int schedulerSelectionCount;
        public double elapsedMilliseconds;
        public double maximumObservedFrameMilliseconds;
        public double initialEstimatedMillisecondsPerState;
        public double observedMillisecondsPerState;
        public double minimumSlackMilliseconds;
        public bool deadlineMissed;
        public PsoFrameStatistics warmupFrameTimes;
        public int[] batchSizes = Array.Empty<int>();
        public double[] batchDurationsMilliseconds = Array.Empty<double>();
        public bool completed;
        public string error;
    }

    [Serializable]
    public sealed class PsoCacheMissTraceReceipt
    {
        public bool requested;
        public bool armed;
        public string scope = "plan";
        public int baselineGraphicsStates;
        public int observedGraphicsStates;
        public int cacheMissGraphicsStates;
        public bool collectionContainsBaseline;
        public string collectionFile;
        public string collectionSha256;
        public string error;
    }

    [Serializable]
    public sealed class PsoWarmupRunReceipt
    {
        public int schemaVersion = PsoConstants.SchemaVersion;
        public string packageVersion = PsoConstants.PackageVersion;
        public string runId;
        public string startedUtc;
        public string endedUtc;
        public string planFile;
        public string planSha256;
        public string strategy;
        public int asyncPsoJobCount;
        public int processorCount;
        public double elapsedMilliseconds;
        public bool completed;
        public string error;
        public PsoEnvironmentSnapshot environment;
        public PsoWarmupPhaseReceipt[] phases = Array.Empty<PsoWarmupPhaseReceipt>();
        public PsoCacheMissTraceReceipt cacheMissTrace;
    }
}
