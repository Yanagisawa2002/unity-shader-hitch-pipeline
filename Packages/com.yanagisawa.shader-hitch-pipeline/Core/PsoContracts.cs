using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    [Serializable]
    public sealed class PsoDriverModuleIdentity
    {
        public string file;
        public string sha256;
    }

    [Serializable]
    public sealed class PsoEnvironmentSnapshot
    {
        public string unityVersion;
        public string engineName;
        public string engineVersion;
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
        public string operatingSystem;
        public int graphicsDeviceId;
        public int graphicsDeviceVendorId;
        public string processorType;
        public int processorCount;
        public string renderingThreadingMode;
        public string costExecutionContext;
        public string driverIdentity;
        public string driverIdentitySource;
        public string driverVersion;
        public string driverIdentityError;
        public string driverRegistryIdentity;
        public PsoDriverModuleIdentity[] driverModules = Array.Empty<PsoDriverModuleIdentity>();
        public PsoContentIdentity identity;

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
        public string adapterId = "unity.graphics-state-collection";
        public string artifactType = "graphics-state-collection";
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
        public int bootstrapBatchSize = 1;
        public double budgetSafetyMarginMilliseconds = 2.0;
        public double budgetCostSafetyMultiplier = 1.5;
        public int budgetCooldownFrames = 8;
        public bool preinteractiveBootstrap = true;
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
        public string adapterId = "unity.graphics-state-collection";
        public string adapterVersion = "1";
        public int sourceSessionCount;
        public string[] sourceSessionHashes = Array.Empty<string>();
        public PsoWarmupPhasePlan[] phases = Array.Empty<PsoWarmupPhasePlan>();
        public string planSha256;
        public PsoCompatibilityContract compatibility;
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
        public bool shaderFilterApplied;
        public string[] shaderAllowlist = Array.Empty<string>();
        public int sourceVariantCount;
        public int sourceGraphicsStateCount;
        public int filteredVariantCount;
        public int filteredGraphicsStateCount;
        public int excludedVariantCount;
        public int excludedGraphicsStateCount;
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
        public int actualSampleFrames;
        public bool scenarioMeasurementGated;
        public double measurementDurationSeconds;
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
        public string backendSchedulingMode = "progressive-batches";
        public int hotSetTier;
        public double deadlineMilliseconds;
        public double expectedUseProbability;
        public int totalGraphicsStates;
        public int completedGraphicsStates;
        public int completedWarmupPermutations;
        public bool backendReportedWarmedUp;
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
        public string budgetPolicy = "strict-admission";
        public double hardFrameBudgetMilliseconds;
        public double budgetSafetyMarginMilliseconds;
        public double budgetCostSafetyMultiplier;
        public int bootstrapBatchSize;
        public bool preinteractiveBootstrapEnabled;
        public int preinteractiveBootstrapBatchCount;
        public double preinteractiveBootstrapMilliseconds;
        public double coldStartBatchMilliseconds;
        public int budgetViolationCount;
        public int minimumBatchBudgetViolationCount;
        public int circuitBreakerTripCount;
        public int deferredFrameCount;
        public int deadlineInfeasibleBatchCount;
        public bool hardFrameBudgetMet;
        public bool hardFrameBudgetFeasible;
        public bool schedulerAdmissionBudgetMet;
        public double maximumBudgetOverrunMilliseconds;
        public string hardFrameBudgetOutcome;
        public string hardBudgetGuaranteeScope =
            "interactive-frames-after-preinteractive-bootstrap; opaque-backend-admission-not-preemptible";
        public PsoFrameStatistics warmupFrameTimes;
        public double[] warmupFrameTimeSamplesMilliseconds = Array.Empty<double>();
        public int[] batchSizes = Array.Empty<int>();
        public int[] safeBatchSizes = Array.Empty<int>();
        public int[] deadlineBatchSizes = Array.Empty<int>();
        public double[] predictedBatchDurationsMilliseconds = Array.Empty<double>();
        public double[] batchDurationsMilliseconds = Array.Empty<double>();
        public double predictedBackgroundCompletionMilliseconds;
        public double observedBackgroundCompletionMilliseconds;
        public string[] admissionReasons = Array.Empty<string>();
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
