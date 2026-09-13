using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    // Exact v3 field order preserves historical JsonUtility plan hashes.
    [Serializable]
    internal sealed class PsoLegacyPlanHashDocument
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
    }
}
