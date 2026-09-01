using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoPlanValidation
    {
        public static string ComputeContentHash(PsoWarmupPlanDocument plan)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            string original = plan.planSha256;
            try
            {
                plan.planSha256 = string.Empty;
                return PsoFileUtility.ComputeTextSha256(JsonUtility.ToJson(plan, true));
            }
            finally
            {
                plan.planSha256 = original;
            }
        }

        public static List<string> Validate(
            PsoWarmupPlanDocument plan,
            string planPath,
            bool validateEnvironment,
            bool validateFiles)
        {
            var issues = new List<string>();
            if (plan == null)
            {
                issues.Add("Plan is null.");
                return issues;
            }

            if (plan.schemaVersion != PsoConstants.SchemaVersion)
                issues.Add("Unsupported schemaVersion: " + plan.schemaVersion + ".");
            if (string.IsNullOrWhiteSpace(plan.profileId))
                issues.Add("profileId is required.");
            if (plan.phases == null || plan.phases.Length == 0)
                issues.Add("At least one warmup phase is required.");

            string expectedPlanHash = ComputeContentHash(plan);
            if (string.IsNullOrWhiteSpace(plan.planSha256))
                issues.Add("planSha256 is missing.");
            else if (!string.Equals(
                         expectedPlanHash,
                         plan.planSha256,
                         StringComparison.OrdinalIgnoreCase))
                issues.Add("planSha256 does not match plan contents.");

            if (validateEnvironment)
            {
                string currentPlatform = Application.platform.ToString();
                string currentApi = SystemInfo.graphicsDeviceType.ToString();
                string currentQuality = CurrentQualityName();
                if (!string.Equals(plan.runtimePlatform, currentPlatform, StringComparison.Ordinal))
                    issues.Add("Plan platform " + plan.runtimePlatform +
                               " does not match " + currentPlatform + ".");
                if (!string.Equals(plan.graphicsDeviceType, currentApi, StringComparison.Ordinal))
                    issues.Add("Plan graphics API " + plan.graphicsDeviceType +
                               " does not match " + currentApi + ".");
                if (!string.IsNullOrWhiteSpace(plan.qualityLevelName) &&
                    !string.Equals(plan.qualityLevelName, currentQuality, StringComparison.Ordinal))
                    issues.Add("Plan quality " + plan.qualityLevelName +
                               " does not match " + currentQuality + ".");
            }

            string planDirectory = string.IsNullOrWhiteSpace(planPath)
                ? string.Empty
                : Path.GetDirectoryName(Path.GetFullPath(planPath));
            var phases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plan.phases != null)
            {
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

                    if (!validateFiles || string.IsNullOrEmpty(planDirectory) ||
                        string.IsNullOrWhiteSpace(phase.collectionFile))
                        continue;

                    try
                    {
                        string collectionPath = PsoFileUtility.ResolveChildPath(
                            planDirectory,
                            phase.collectionFile);
                        if (!File.Exists(collectionPath))
                        {
                            issues.Add(prefix + " file is missing: " + collectionPath);
                            continue;
                        }

                        string hash = PsoFileUtility.ComputeSha256(collectionPath);
                        if (!string.Equals(
                                hash,
                                phase.collectionSha256,
                                StringComparison.OrdinalIgnoreCase))
                            issues.Add(prefix + " collection hash does not match.");
                    }
                    catch (Exception exception)
                    {
                        issues.Add(prefix + " file is invalid: " + exception.Message);
                    }
                }
            }

            return issues;
        }

        public static PsoWarmupPlanDocument LoadAndValidate(
            string path,
            bool validateEnvironment,
            bool validateFiles)
        {
            PsoWarmupPlanDocument plan = PsoFileUtility.ReadJson<PsoWarmupPlanDocument>(path);
            List<string> issues = Validate(plan, path, validateEnvironment, validateFiles);
            if (issues.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, issues));
            return plan;
        }

        private static string CurrentQualityName()
        {
            int qualityIndex = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            return qualityIndex >= 0 && qualityIndex < names.Length
                ? names[qualityIndex]
                : "Unknown";
        }
    }
}
