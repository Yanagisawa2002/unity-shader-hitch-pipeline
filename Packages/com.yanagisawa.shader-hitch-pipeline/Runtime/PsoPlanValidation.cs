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
            List<string> issues = PsoPlanRules.Validate(plan);
            if (plan == null)
                return issues;

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
