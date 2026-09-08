using System;
using System.Collections.Generic;
using System.Linq;

namespace Yanagisawa.ShaderHitchPipeline
{
    [Serializable]
    public sealed class PsoStartupHotsetDocument
    {
        public int schemaVersion = 1;
        public string planSha256;
        public double startupBudgetMilliseconds;
        public PsoHotsetUnit[] units = Array.Empty<PsoHotsetUnit>();
        public PsoHotsetTrace[] training = Array.Empty<PsoHotsetTrace>();
    }

    public static class PsoStartupHotset
    {
        public static PsoHotsetDecision PrepareValidated(PsoWarmupPlanDocument plan, string planHash,
            PsoStartupHotsetDocument document,
            Func<PsoWarmupPlanDocument, PsoStartupHotsetDocument, bool> validateCompatibility)
        {
            bool compatible = false;
            try { compatible = document != null && validateCompatibility != null && validateCompatibility(plan, document); }
            catch (Exception) { /* Unknown proof is a required-only fallback, not compatibility. */ }
            var result = Prepare(plan, planHash, compatible ? document : null);
            if (!compatible) result.rejectedTraces = new[] { "collection-or-cost-compatibility-unproven:required-only-fallback" };
            return result;
        }

        // Call only after normal current-build/environment validation of the plan.
        // A policy file is selection evidence, never permission to load a stale plan.
        public static PsoHotsetDecision Prepare(PsoWarmupPlanDocument plan, string planHash,
            PsoStartupHotsetDocument document)
        {
            if (plan == null || plan.phases == null) throw new ArgumentNullException(nameof(plan));
            var units = new List<PsoHotsetUnit>();
            bool valid = document != null && document.schemaVersion == 1 &&
                !string.IsNullOrWhiteSpace(planHash) && document.planSha256 == planHash &&
                document.units != null && document.units.Length == plan.phases.Length;
            foreach (var phase in plan.phases)
            {
                var supplied = document?.units?.Where(u => u != null && u.phase == phase.phase).ToArray();
                var source = supplied != null && supplied.Length == 1 ? supplied[0] : null;
                bool matches = source != null && source.collectionSha256 == phase.collectionSha256 &&
                    source.graphicsStateCount == phase.graphicsStateCount && !string.IsNullOrWhiteSpace(source.id);
                valid &= matches;
                units.Add(new PsoHotsetUnit
                {
                    id = matches ? source.id : phase.phase,
                    phase = phase.phase,
                    requiredStartup = phase.required && phase.prewarmAtStartup,
                    collectionSha256 = phase.collectionSha256,
                    graphicsStateCount = phase.graphicsStateCount,
                    contentId = matches ? source.contentId : null,
                    contentRevision = matches ? source.contentRevision : null,
                    compatibilityNamespace = matches ? source.compatibilityNamespace : null,
                    estimatedWarmupMilliseconds = matches ? source.estimatedWarmupMilliseconds : -1,
                    residentBytes = matches ? source.residentBytes : -1
                });
            }
            double budget = document?.startupBudgetMilliseconds ?? 0;
            if (double.IsNaN(budget) || double.IsInfinity(budget) || budget < 0) { valid = false; budget = 0; }
            // Malformed policy must never suppress caller-required phases, including
            // duplicate attacker/user supplied IDs that make normal selection invalid.
            if (units.Select(u => u.id).Distinct(StringComparer.Ordinal).Count() != units.Count) valid = false;
            if (!valid)
                foreach (var unit in units) { unit.id = unit.phase; unit.estimatedWarmupMilliseconds = -1; }
            var result = PsoHotsetPolicy.Select(units.ToArray(), valid ? document.training : null, budget);
            if (!valid) result.rejectedTraces = new[] { "startup-policy-invalid:required-only-fallback" };
            // Runtime activation uses phases; stable content IDs remain in the policy
            // and core selection/replay contract, avoiding case-insensitive aliasing.
            var byId = units.ToDictionary(u => u.id, StringComparer.Ordinal);
            result.startupUnitIds = result.startupUnitIds.Select(id => byId[id].phase).ToArray();
            result.deferredUnitIds = result.deferredUnitIds.Select(id => byId[id].phase).ToArray();
            result.policy += ":runtime-phase-ids";
            return result;
        }
    }
}
