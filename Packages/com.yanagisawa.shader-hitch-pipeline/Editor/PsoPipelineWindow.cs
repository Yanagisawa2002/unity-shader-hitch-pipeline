using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline.Editor
{
    public sealed class PsoPipelineWindow : EditorWindow
    {
        private PsoProjectConfiguration configuration;
        private Vector2 scroll;
        private string status = "Ready.";

        [MenuItem("Tools/Shader Hitch Pipeline/Control Center")]
        public static void Open()
        {
            GetWindow<PsoPipelineWindow>("Shader Hitch Pipeline");
        }

        private void OnEnable()
        {
            configuration = PsoProjectConfiguration.Load();
        }

        private void OnGUI()
        {
            if (configuration == null)
                configuration = PsoProjectConfiguration.Load();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Trace inbox and deterministic profile", EditorStyles.boldLabel);
            configuration.inboxDirectory = EditorGUILayout.TextField(
                "Inbox", configuration.inboxDirectory);
            configuration.profileOutputDirectory = EditorGUILayout.TextField(
                "Profile output", configuration.profileOutputDirectory);
            configuration.installedPlanDirectory = EditorGUILayout.TextField(
                "Installed plan", configuration.installedPlanDirectory);
            configuration.profileId = EditorGUILayout.TextField(
                "Profile ID", configuration.profileId);
            configuration.targetRuntimePlatform = EditorGUILayout.TextField(
                "Runtime platform", configuration.targetRuntimePlatform);
            configuration.targetGraphicsDeviceType = EditorGUILayout.TextField(
                "Graphics API", configuration.targetGraphicsDeviceType);
            configuration.targetQualityLevelName = EditorGUILayout.TextField(
                "Quality (blank = auto)", configuration.targetQualityLevelName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Progressive warmup budget", EditorStyles.boldLabel);
            configuration.initialBatchSize = EditorGUILayout.IntField(
                "Initial batch", configuration.initialBatchSize);
            configuration.minimumBatchSize = EditorGUILayout.IntField(
                "Minimum batch", configuration.minimumBatchSize);
            configuration.maximumBatchSize = EditorGUILayout.IntField(
                "Maximum batch", configuration.maximumBatchSize);
            configuration.targetFrameMilliseconds = EditorGUILayout.DoubleField(
                "Frame budget (ms)", configuration.targetFrameMilliseconds);
            configuration.startupDeadlineMilliseconds = EditorGUILayout.DoubleField(
                "Startup deadline (ms)", configuration.startupDeadlineMilliseconds);
            configuration.deferredDeadlineMilliseconds = EditorGUILayout.DoubleField(
                "Deferred deadline (ms)", configuration.deferredDeadlineMilliseconds);
            configuration.estimatedMillisecondsPerState = EditorGUILayout.DoubleField(
                "Estimated ms / state", configuration.estimatedMillisecondsPerState);
            configuration.bootstrapBatchSize = EditorGUILayout.IntField(
                "Cold bootstrap batch", configuration.bootstrapBatchSize);
            configuration.preinteractiveBootstrap = EditorGUILayout.Toggle(
                "Gate startup hot set before first frame",
                configuration.preinteractiveBootstrap);
            configuration.budgetSafetyMarginMilliseconds =
                EditorGUILayout.DoubleField(
                    "Budget safety margin (ms)",
                    configuration.budgetSafetyMarginMilliseconds);
            configuration.budgetCostSafetyMultiplier =
                EditorGUILayout.DoubleField(
                    "Cost safety multiplier",
                    configuration.budgetCostSafetyMultiplier);
            configuration.budgetCooldownFrames = EditorGUILayout.IntField(
                "Violation cooldown frames", configuration.budgetCooldownFrames);
            configuration.startupExpectedUseProbability = EditorGUILayout.Slider(
                "Startup use probability",
                (float)configuration.startupExpectedUseProbability,
                0.0f,
                1.0f);
            configuration.deferredExpectedUseProbability = EditorGUILayout.Slider(
                "Deferred use probability",
                (float)configuration.deferredExpectedUseProbability,
                0.0f,
                1.0f);
            configuration.startupHotSetTier = EditorGUILayout.IntField(
                "Startup hot-set tier", configuration.startupHotSetTier);
            configuration.deferredHotSetTier = EditorGUILayout.IntField(
                "Deferred hot-set tier", configuration.deferredHotSetTier);
            configuration.validateInstalledPlanBeforeBuild = EditorGUILayout.Toggle(
                "Fail invalid builds", configuration.validateInstalledPlanBeforeBuild);

            EditorGUILayout.Space();
            if (GUILayout.Button("Save configuration"))
                Run(() => { configuration.Save(); return "Configuration saved."; });
            if (GUILayout.Button("1. Process inbox and merge"))
                Run(() =>
                {
                    configuration.Save();
                    PsoInboxProcessingResult result = PsoInboxProcessor.Process(configuration);
                    return "Generated " + result.planPath;
                });
            if (GUILayout.Button("2. Install latest profile into StreamingAssets"))
                Run(() =>
                {
                    string plan = Path.Combine(
                        configuration.ResolveProjectPath(configuration.profileOutputDirectory),
                        PsoFileUtility.SanitizeFileName(configuration.profileId),
                        PsoConstants.DefaultPlanFileName);
                    return "Installed " + PsoPlanInstaller.Install(plan, configuration);
                });
            if (GUILayout.Button("3. Validate installed plan"))
                Run(() =>
                {
                    PsoWarmupPlanDocument plan = PsoPlanInstaller.ValidateInstalled(configuration);
                    return "Valid: " + plan.profileId + " / " + plan.planSha256;
                });

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(status, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void Run(Func<string> action)
        {
            try
            {
                status = action();
                Debug.Log("[ShaderHitchPipeline] " + status);
            }
            catch (Exception exception)
            {
                status = exception.Message;
                Debug.LogException(exception);
            }
        }
    }
}
