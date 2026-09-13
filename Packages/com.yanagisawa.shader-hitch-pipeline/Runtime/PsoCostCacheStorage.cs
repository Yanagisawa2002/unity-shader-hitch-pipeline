using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoCostCacheStorage
    {
        public static string ComputeHash(PsoCostCacheDocument cache)
        {
            string original = cache.cacheSha256;
            try { cache.cacheSha256 = string.Empty; return PsoFileUtility.ComputeTextSha256(PsoDocumentJson.Serialize(cache, false)); }
            finally { cache.cacheSha256 = original; }
        }

        public static bool TryApply(string path, PsoWarmupPlanDocument plan, out string[] reasons)
        {
            try
            {
                var cache = PsoFileUtility.ReadJson<PsoCostCacheDocument>(path);
                var issues = PsoCostCache.Validate(cache, plan, PsoUnityEnvironment.Capture());
                if (cache == null || !string.Equals(ComputeHash(cache), cache.cacheSha256, StringComparison.OrdinalIgnoreCase))
                    issues.Add("Cost cache integrity mismatch.");
                reasons = issues.ToArray();
                if (issues.Count != 0) return false;
                foreach (PsoCostCacheEntry entry in cache.entries)
                    Array.Find(plan.phases, phase => phase.phase == entry.phase).estimatedMillisecondsPerState = entry.millisecondsPerState;
                // Adaptive policy still cold-probes; a cache never asserts a hard driver bound.
                return true;
            }
            catch (Exception exception) { reasons = new[] { "Cost cache unavailable: " + exception.Message }; return false; }
        }

        public static void Save(string path, PsoWarmupPlanDocument plan, PsoCostCacheEntry[] measuredEntries)
        {
            var cache = new PsoCostCacheDocument
            {
                version = 1, modelVersion = PsoCompatibility.CostModelVersion,
                planSha256 = plan.planSha256, environment = PsoUnityEnvironment.Capture(), entries = measuredEntries
            };
            List<string> issues = PsoCostCache.Validate(cache, plan, cache.environment);
            if (issues.Count != 0) throw new ArgumentException(string.Join("\n", issues));
            cache.cacheSha256 = ComputeHash(cache);
            PsoFileUtility.WriteJsonAtomic(path, cache);
        }
    }
}
