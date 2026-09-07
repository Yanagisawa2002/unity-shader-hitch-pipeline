using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoUnityIntegratedCompatibility
    {
        // Explicit application validators may implement another declared, measured
        // cost model. This default accepts only the package's verified cost cache.
        public static bool ValidateConfiguredHotset(PsoWarmupPlanDocument plan, PsoStartupHotsetDocument policy)
        {
            string path = PsoCommandLine.Current.GetString("-pso-cost-cache", string.Empty);
            return ValidateHotsetWithCostCache(path, plan, policy);
        }

        public static bool ValidateHotsetWithCostCache(string cachePath, PsoWarmupPlanDocument plan,
            PsoStartupHotsetDocument policy)
        {
            if (string.IsNullOrWhiteSpace(cachePath)) return false;
            try
            {
                var cache = PsoFileUtility.ReadJson<PsoCostCacheDocument>(cachePath);
                if (cache == null || !string.Equals(PsoCostCacheStorage.ComputeHash(cache), cache.cacheSha256,
                    StringComparison.OrdinalIgnoreCase)) return false;
                return PsoIntegratedCompatibility.ValidateHotsetCostProof(plan, policy, cache,
                    PsoUnityEnvironment.Capture()).Count == 0;
            }
            catch (Exception) { return false; }
        }
    }
}
