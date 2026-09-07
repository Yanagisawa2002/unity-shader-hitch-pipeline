using System;
using System.Collections.Generic;

namespace Yanagisawa.ShaderHitchPipeline
{
    [Serializable]
    public sealed class PsoCostCacheEntry
    {
        public string phase;
        public string collectionSha256;
        public double millisecondsPerState;
        public int observedBatches;
    }

    [Serializable]
    public sealed class PsoCostCacheDocument
    {
        public int version;
        public string modelVersion;
        public string planSha256;
        public PsoEnvironmentSnapshot environment;
        public PsoCostCacheEntry[] entries = Array.Empty<PsoCostCacheEntry>();
        public string cacheSha256;
    }

    public static class PsoCostCache
    {
        // Caller verifies serialized cache integrity before this engine-neutral gate.
        // All entries are checked before any prior is applied; partial application is forbidden.
        public static List<string> Validate(PsoCostCacheDocument cache,
            PsoWarmupPlanDocument plan, PsoEnvironmentSnapshot current)
        {
            var issues = new List<string>();
            if (cache == null || plan == null || plan.compatibility == null)
            { issues.Add("Cost cache requires an identity-bound plan and cache."); return issues; }
            if (cache.version != 1) issues.Add("Unsupported cost cache version.");
            if (cache.modelVersion != PsoCompatibility.CostModelVersion) issues.Add("Cost model version changed.");
            if (string.IsNullOrWhiteSpace(cache.planSha256) || cache.planSha256 != plan.planSha256)
                issues.Add("Cost cache belongs to a different plan.");
            PsoCompatibilityResult match = PsoCompatibility.Evaluate(new PsoCompatibilityContract
            {
                version = plan.compatibility.version,
                collectionEnvironment = plan.compatibility.collectionEnvironment,
                costEnvironment = cache.environment, costModelVersion = cache.modelVersion
            }, current);
            issues.AddRange(match.collectionReasons);
            issues.AddRange(match.costReasons);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cache.entries == null || cache.entries.Length == 0) issues.Add("Cost cache has no measured entries.");
            else foreach (PsoCostCacheEntry entry in cache.entries)
            {
                if (entry == null) { issues.Add("Null cost entry."); continue; }
                if (string.IsNullOrWhiteSpace(entry.phase) || !seen.Add(entry.phase)) issues.Add("Missing or duplicate cost phase.");
                if (entry.observedBatches < 1 || entry.millisecondsPerState <= 0 ||
                    double.IsNaN(entry.millisecondsPerState) || double.IsInfinity(entry.millisecondsPerState))
                    issues.Add("Cost entry has no valid measured estimate.");
                PsoWarmupPhasePlan phase = Array.Find(plan.phases ?? Array.Empty<PsoWarmupPhasePlan>(),
                    item => item != null && item.phase == entry.phase);
                if (phase == null || string.IsNullOrWhiteSpace(entry.collectionSha256) ||
                    !string.Equals(phase.collectionSha256, entry.collectionSha256, StringComparison.OrdinalIgnoreCase))
                    issues.Add("Cost entry collection changed or is missing.");
            }
            return issues;
        }
    }
}
