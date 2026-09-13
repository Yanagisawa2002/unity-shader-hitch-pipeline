using System;
using System.Collections.Generic;
using System.IO;

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>Cross-module checks; artifact integrity and loaded-content attestation remain caller obligations.</summary>
    public static class PsoIntegratedCompatibility
    {
        public static string RequireCollectionNamespace(PsoCompatibilityContract recorded, PsoEnvironmentSnapshot current)
        {
            PsoCompatibilityResult result = PsoCompatibility.Evaluate(recorded, current);
            if (!result.collectionCompatible)
                throw new InvalidDataException("Collection compatibility unproven: " + string.Join("; ", result.collectionReasons));
            return PsoCompatibility.CollectionKey(current);
        }

        /// <summary>Author a policy from a measured cache. Runtime must independently check the current environment.</summary>
        public static PsoStartupHotsetDocument CreateCostBackedPolicy(PsoWarmupPlanDocument plan, string planFileHash,
            PsoCostCacheDocument cache, PsoHotsetTrace[] training, double startupBudgetMilliseconds)
        {
            var issues = PsoCostCache.Validate(cache, plan, cache == null ? null : cache.environment);
            if (issues.Count != 0) throw new InvalidDataException(string.Join("; ", issues));
            if (string.IsNullOrWhiteSpace(planFileHash) || double.IsNaN(startupBudgetMilliseconds) ||
                double.IsInfinity(startupBudgetMilliseconds) || startupBudgetMilliseconds < 0)
                throw new ArgumentException("A plan file hash and finite nonnegative startup budget are required.");
            string identity = RequireCollectionNamespace(plan.compatibility, cache.environment);
            var units = new List<PsoHotsetUnit>();
            foreach (PsoWarmupPhasePlan phase in plan.phases)
            {
                PsoCostCacheEntry entry = Array.Find(cache.entries, value => value.phase == phase.phase);
                units.Add(new PsoHotsetUnit
                {
                    id = phase.phase, phase = phase.phase,
                    contentId = cache.environment.identity.contentId,
                    contentRevision = cache.environment.identity.contentRevision,
                    compatibilityNamespace = identity, collectionSha256 = phase.collectionSha256,
                    graphicsStateCount = phase.graphicsStateCount,
                    requiredStartup = phase.required && phase.prewarmAtStartup,
                    estimatedWarmupMilliseconds = entry == null ? -1 : entry.millisecondsPerState * phase.graphicsStateCount,
                    residentBytes = -1
                });
            }
            return new PsoStartupHotsetDocument
            {
                planSha256 = planFileHash, startupBudgetMilliseconds = startupBudgetMilliseconds,
                units = units.ToArray(), training = training ?? Array.Empty<PsoHotsetTrace>()
            };
        }

        /// <summary>Only matched measured cache estimates authorize optional cost-based selection.
        /// Unknown estimates can remain -1; required startup phases are preserved by PrepareValidated.</summary>
        public static List<string> ValidateHotsetCostProof(PsoWarmupPlanDocument plan, PsoStartupHotsetDocument policy,
            PsoCostCacheDocument cache, PsoEnvironmentSnapshot current)
        {
            var issues = PsoCostCache.Validate(cache, plan, current);
            if (issues.Count != 0) return issues;
            if (policy == null || policy.units == null) { issues.Add("Hotset policy units are missing."); return issues; }
            string identity = RequireCollectionNamespace(plan.compatibility, current);
            foreach (PsoHotsetUnit unit in policy.units)
            {
                if (unit == null) { issues.Add("Null hotset unit."); continue; }
                if (unit.compatibilityNamespace != identity || unit.contentId != current.identity.contentId ||
                    unit.contentRevision != current.identity.contentRevision)
                    issues.Add("Hotset unit content namespace/revision changed: " + unit.id);
                PsoWarmupPhasePlan phase = Array.Find(plan.phases, value => value.phase == unit.phase);
                PsoCostCacheEntry entry = Array.Find(cache.entries, value => value.phase == unit.phase);
                if (phase == null || unit.collectionSha256 != phase.collectionSha256 ||
                    unit.graphicsStateCount != phase.graphicsStateCount)
                { issues.Add("Hotset unit does not match its phase: " + unit.id); continue; }
                double expected = entry == null ? -1 : entry.millisecondsPerState * phase.graphicsStateCount;
                if (double.IsNaN(unit.estimatedWarmupMilliseconds) || double.IsInfinity(unit.estimatedWarmupMilliseconds) ||
                    unit.estimatedWarmupMilliseconds != expected)
                    issues.Add("Hotset unit estimate is not backed by the validated measured cache: " + unit.id);
            }
            return issues;
        }
    }
}
