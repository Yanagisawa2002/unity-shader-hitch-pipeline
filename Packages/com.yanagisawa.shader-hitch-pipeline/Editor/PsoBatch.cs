using System;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline.Editor
{
    public static class PsoBatch
    {
        public static void ProcessInbox()
        {
            PsoProjectConfiguration configuration = LoadWithCommandLineOverrides();
            PsoInboxProcessingResult result = PsoInboxProcessor.Process(configuration);
            Debug.Log("[ShaderHitchPipeline] Generated plan: " + result.planPath);
            Debug.Log("[ShaderHitchPipeline] Merge receipt: " + result.receiptPath);
        }

        public static void InstallPlan()
        {
            PsoProjectConfiguration configuration = LoadWithCommandLineOverrides();
            string defaultPlan = System.IO.Path.Combine(
                configuration.ResolveProjectPath(configuration.profileOutputDirectory),
                PsoFileUtility.SanitizeFileName(configuration.profileId),
                PsoConstants.DefaultPlanFileName);
            string plan = PsoCommandLine.Current.GetString(
                PsoConstants.InstallPlanArgument,
                defaultPlan);
            Debug.Log("[ShaderHitchPipeline] Installed plan: " +
                      PsoPlanInstaller.Install(plan, configuration));
        }

        public static void ValidateInstalledPlan()
        {
            PsoProjectConfiguration configuration = LoadWithCommandLineOverrides();
            PsoWarmupPlanDocument plan = PsoPlanInstaller.ValidateInstalled(configuration);
            Debug.Log("[ShaderHitchPipeline] Installed plan is valid: " +
                      plan.profileId + " / " + plan.planSha256);
        }

        public static PsoProjectConfiguration LoadWithCommandLineOverrides()
        {
            PsoProjectConfiguration configuration = PsoProjectConfiguration.Load();
            PsoCommandLine commandLine = PsoCommandLine.Current;
            configuration.inboxDirectory = commandLine.GetString(
                PsoConstants.InboxArgument,
                configuration.inboxDirectory);
            configuration.profileOutputDirectory = commandLine.GetString(
                PsoConstants.ProfileOutputArgument,
                configuration.profileOutputDirectory);
            configuration.profileId = commandLine.GetString(
                PsoConstants.ProfileArgument,
                configuration.profileId);
            return configuration;
        }
    }
}
