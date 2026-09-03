using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yanagisawa.ShaderHitchPipeline.Editor
{
    public sealed class PsoBuildValidator : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static string validatedPlanPath;
        private static string validatedPlanHash;
        private static PsoWarmupPlanDocument validatedPlan;
        private static string[] validatedApis;

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            validatedPlan = null;
            validatedPlanPath = string.Empty;
            validatedPlanHash = string.Empty;
            validatedApis = null;
            bool trainingBuild =
                PsoCommandLine.Current.HasFlag(PsoConstants.TrainingBuildArgument);
            bool baselineBuild =
                PsoCommandLine.Current.HasFlag(PsoConstants.BaselineBuildArgument);
            if (trainingBuild || baselineBuild)
            {
                Debug.Log(
                    "[ShaderHitchPipeline] " +
                    (trainingBuild ? "Training" : "Cold-baseline") +
                    " build intentionally bypasses the installed warmup-plan gate.");
                return;
            }

            PsoProjectConfiguration configuration = PsoProjectConfiguration.Load();
            if (!configuration.validateInstalledPlanBeforeBuild)
                return;

            try
            {
                validatedPlanPath = PsoPlanInstaller.GetInstalledPlanPath(configuration);
                validatedPlan = PsoPlanInstaller.ValidateInstalled(configuration);
                validatedPlanHash = PsoFileUtility.ComputeSha256(validatedPlanPath);
                ValidateTarget(report.summary.platform, validatedPlan, out validatedApis);
                Debug.Log("[ShaderHitchPipeline] Build gate accepted plan '" +
                          validatedPlan.profileId + "' (" + validatedPlan.planSha256 + ").");
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    "Shader Hitch Pipeline build gate failed: " + exception.Message);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (validatedPlan == null)
                return;

            string receipts = Path.GetFullPath("PsoArtifacts/BuildReceipts");
            string file = Path.Combine(
                receipts,
                "build-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
            var receipt = new PsoBuildReceipt
            {
                buildGuid = report.summary.guid.ToString(),
                generatedUtc = PsoFileUtility.UtcNowText(),
                buildTarget = report.summary.platform.ToString(),
                outputPath = report.summary.outputPath,
                result = report.summary.result.ToString(),
                planFile = validatedPlanPath,
                planSha256 = validatedPlanHash,
                runtimePlatform = validatedPlan.runtimePlatform,
                graphicsDeviceType = validatedPlan.graphicsDeviceType,
                enabledGraphicsApis = validatedApis ?? Array.Empty<string>(),
            };
            PsoFileUtility.WriteJsonAtomic(file, receipt);
            Debug.Log("[ShaderHitchPipeline] Build receipt: " + file);
            validatedPlan = null;
        }

        public static void ValidateTarget(
            BuildTarget buildTarget,
            PsoWarmupPlanDocument plan,
            out string[] graphicsApis)
        {
            RuntimePlatform expectedPlatform;
            switch (buildTarget)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    expectedPlatform = RuntimePlatform.WindowsPlayer;
                    break;
                case BuildTarget.StandaloneOSX:
                    expectedPlatform = RuntimePlatform.OSXPlayer;
                    break;
                case BuildTarget.StandaloneLinux64:
                    expectedPlatform = RuntimePlatform.LinuxPlayer;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Build target is not supported by this version: " + buildTarget + ".");
            }

            if (!string.Equals(
                    plan.runtimePlatform,
                    expectedPlatform.ToString(),
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Plan targets " + plan.runtimePlatform +
                    " but build target runs as " + expectedPlatform + ".");

            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(buildTarget);
            graphicsApis = apis.Select(api => api.ToString()).ToArray();
            if (!Enum.TryParse(plan.graphicsDeviceType, out GraphicsDeviceType planApi))
                throw new InvalidDataException(
                    "Plan has unknown graphics API " + plan.graphicsDeviceType + ".");
            if (!apis.Contains(planApi))
                throw new InvalidDataException(
                    "Plan graphics API " + planApi +
                    " is not enabled for build target " + buildTarget + ".");
        }
    }
}
