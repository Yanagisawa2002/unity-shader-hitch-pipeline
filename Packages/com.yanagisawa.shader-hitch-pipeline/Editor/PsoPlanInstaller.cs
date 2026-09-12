using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

namespace Yanagisawa.ShaderHitchPipeline.Editor
{
    public static class PsoPlanInstaller
    {
        public static string Install(
            string sourcePlanPath,
            PsoProjectConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            configuration.ValidateSettings();

            sourcePlanPath = Path.GetFullPath(sourcePlanPath);
            PsoWarmupPlanDocument plan = PsoPlanValidation.LoadAndValidate(
                sourcePlanPath,
                false,
                true);
            string destination = configuration.ResolveProjectPath(
                configuration.installedPlanDirectory);
            EnsureDestinationIsUnderAssets(destination);

            string parent = Path.GetDirectoryName(destination);
            string staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
            string backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);

            try
            {
                string sourceDirectory = Path.GetDirectoryName(sourcePlanPath);
                for (int index = 0; index < plan.phases.Length; index++)
                {
                    string sourceCollection = PsoFileUtility.ResolveChildPath(
                        sourceDirectory,
                        plan.phases[index].collectionFile);
                    string destinationCollection = PsoFileUtility.ResolveChildPath(
                        staging,
                        plan.phases[index].collectionFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationCollection));
                    File.Copy(sourceCollection, destinationCollection, true);
                }

                string stagedPlan = Path.Combine(staging, PsoConstants.DefaultPlanFileName);
                File.Copy(sourcePlanPath, stagedPlan, true);
                List<string> issues = PsoPlanValidation.Validate(plan, stagedPlan, false, true);
                if (issues.Count > 0)
                    throw new InvalidDataException(string.Join(Environment.NewLine, issues));
                ValidateCollectionMetadata(plan, stagedPlan);

                Directory.CreateDirectory(parent);
                if (Directory.Exists(destination))
                    Directory.Move(destination, backup);
                Directory.Move(staging, destination);
                if (Directory.Exists(backup))
                    Directory.Delete(backup, true);
            }
            catch
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
                if (!Directory.Exists(destination) && Directory.Exists(backup))
                    Directory.Move(backup, destination);
                throw;
            }
            finally
            {
                AssetDatabase.Refresh();
            }

            return Path.Combine(destination, PsoConstants.DefaultPlanFileName);
        }

        public static string GetInstalledPlanPath(PsoProjectConfiguration configuration)
        {
            return Path.Combine(
                configuration.ResolveProjectPath(configuration.installedPlanDirectory),
                PsoConstants.DefaultPlanFileName);
        }

        public static PsoWarmupPlanDocument ValidateInstalled(
            PsoProjectConfiguration configuration)
        {
            string path = GetInstalledPlanPath(configuration);
            PsoWarmupPlanDocument plan = PsoPlanValidation.LoadAndValidate(
                path,
                false,
                true);
            ValidateCollectionMetadata(plan, path);
            return plan;
        }

        public static void ValidateCollectionMetadata(
            PsoWarmupPlanDocument plan,
            string planPath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(planPath));
            if (!Enum.TryParse(plan.runtimePlatform, out RuntimePlatform platform))
                throw new InvalidDataException(
                    "Plan has unknown runtime platform " + plan.runtimePlatform + ".");
            if (!Enum.TryParse(plan.graphicsDeviceType, out GraphicsDeviceType graphicsApi))
                throw new InvalidDataException(
                    "Plan has unknown graphics API " + plan.graphicsDeviceType + ".");

            for (int index = 0; index < plan.phases.Length; index++)
            {
                PsoWarmupPhasePlan phase = plan.phases[index];
                string path = PsoFileUtility.ResolveChildPath(
                    directory,
                    phase.collectionFile);
                var collection = new GraphicsStateCollection();
                try
                {
                    if (!collection.LoadFromFile(path))
                        throw new InvalidDataException(
                            "Unity could not load collection for phase " + phase.phase + ".");
                    if (collection.runtimePlatform != platform ||
                        collection.graphicsDeviceType != graphicsApi ||
                        !string.Equals(
                            collection.qualityLevelName ?? string.Empty,
                            plan.qualityLevelName ?? string.Empty,
                            StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "Collection metadata does not match plan for phase " + phase.phase + ".");
                    if (collection.variantCount != phase.variantCount ||
                        collection.totalGraphicsStateCount != phase.graphicsStateCount)
                        throw new InvalidDataException(
                            "Collection counts do not match plan for phase " + phase.phase + ".");
                }
                finally { UnityEngine.Object.DestroyImmediate(collection); }
            }
        }

        private static void EnsureDestinationIsUnderAssets(string destination)
        {
            string assets = Path.GetFullPath("Assets")
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string requiredPrefix = assets + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(requiredPrefix, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Installed plan directory must be below this Unity project's Assets directory.");
        }
    }
}
