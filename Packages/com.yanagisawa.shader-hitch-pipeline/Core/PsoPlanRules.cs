using System;
using System.Collections.Generic;

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>
    /// Engine-neutral structural validation for portable warmup plans.
    /// Environment matching, serialization and artifact I/O remain adapter concerns.
    /// </summary>
    public static class PsoPlanRules
    {
        public static List<string> Validate(PsoWarmupPlanDocument plan)
        {
            var issues = new List<string>();
            if (plan == null)
            {
                issues.Add("Plan is null.");
                return issues;
            }

            if (plan.schemaVersion != PsoConstants.SchemaVersion)
                issues.Add("Unsupported schemaVersion: " + plan.schemaVersion + ".");
            if (plan.compatibility != null)
            {
                PsoCompatibilityResult compatibility = PsoCompatibility.Evaluate(
                    plan.compatibility, plan.compatibility.collectionEnvironment);
                issues.AddRange(compatibility.collectionReasons);
                PsoEnvironmentSnapshot recorded = plan.compatibility.collectionEnvironment;
                if (recorded != null && (plan.runtimePlatform != recorded.runtimePlatform ||
                    plan.graphicsDeviceType != recorded.graphicsDeviceType || plan.qualityLevelName != recorded.qualityLevelName))
                    issues.Add("Plan profile differs from collection compatibility environment.");
            }
            if (string.IsNullOrWhiteSpace(plan.profileId))
                issues.Add("profileId is required.");
            if (string.IsNullOrWhiteSpace(plan.adapterId))
                issues.Add("adapterId is required.");
            if (string.IsNullOrWhiteSpace(plan.adapterVersion))
                issues.Add("adapterVersion is required.");
            if (plan.phases == null || plan.phases.Length == 0)
                issues.Add("At least one warmup phase is required.");

            var phases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plan.phases == null)
                return issues;

            for (int index = 0; index < plan.phases.Length; index++)
            {
                PsoWarmupPhasePlan phase = plan.phases[index];
                string prefix = "phases[" + index + "]";
                if (phase == null)
                {
                    issues.Add(prefix + " is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(phase.phase))
                    issues.Add(prefix + ".phase is required.");
                else if (!phases.Add(phase.phase))
                    issues.Add("Duplicate phase: " + phase.phase + ".");
                if (string.IsNullOrWhiteSpace(phase.collectionFile))
                    issues.Add(prefix + ".collectionFile is required.");
                if (phase.minimumBatchSize < 1)
                    issues.Add(prefix + ".minimumBatchSize must be positive.");
                if (phase.maximumBatchSize < phase.minimumBatchSize)
                    issues.Add(prefix + ".maximumBatchSize is below minimumBatchSize.");
                if (phase.initialBatchSize < phase.minimumBatchSize ||
                    phase.initialBatchSize > phase.maximumBatchSize)
                    issues.Add(prefix + ".initialBatchSize is outside its bounds.");
                if (phase.targetFrameMilliseconds <= 0.0)
                    issues.Add(prefix + ".targetFrameMilliseconds must be positive.");
                if (phase.deadlineMilliseconds < 0.0)
                    issues.Add(prefix + ".deadlineMilliseconds cannot be negative.");
                if (phase.estimatedMillisecondsPerState <= 0.0)
                    issues.Add(prefix + ".estimatedMillisecondsPerState must be positive.");
                if (phase.bootstrapBatchSize < phase.minimumBatchSize ||
                    phase.bootstrapBatchSize > phase.maximumBatchSize)
                    issues.Add(prefix + ".bootstrapBatchSize is outside its bounds.");
                if (phase.budgetSafetyMarginMilliseconds < 0.0 ||
                    phase.budgetSafetyMarginMilliseconds >=
                    phase.targetFrameMilliseconds)
                    issues.Add(prefix +
                               ".budgetSafetyMarginMilliseconds must be below the frame budget.");
                if (phase.budgetCostSafetyMultiplier < 1.0)
                    issues.Add(prefix +
                               ".budgetCostSafetyMultiplier must be at least one.");
                if (phase.budgetCooldownFrames < 0)
                    issues.Add(prefix + ".budgetCooldownFrames cannot be negative.");
                if (phase.expectedUseProbability < 0.0 ||
                    phase.expectedUseProbability > 1.0)
                    issues.Add(prefix +
                               ".expectedUseProbability must be between zero and one.");
                if (phase.hotSetTier < 0)
                    issues.Add(prefix + ".hotSetTier cannot be negative.");
            }

            return issues;
        }
    }
}
