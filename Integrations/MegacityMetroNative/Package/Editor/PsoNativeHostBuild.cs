using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Yanagisawa.ShaderHitchPipeline.Editor;

namespace Yanagisawa.ShaderHitchPipeline.NativeScenes.Editor
{
    public static class PsoNativeHostBuild
    {
        // Explicit preparation entry. Calling it starts an Editor import; source-only
        // preparation must not invoke it until the host's disk budget is available.
        public static void ConfigureWindowsD3D12()
        {
            ValidatePinnedHost();
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
            AssetDatabase.SaveAssets();
            Debug.Log("[PSO Native Scenes] Explicit Windows D3D12-only cell configured; retain the settings diff and resolved UPM lock before building.");
        }

        // Explicit build entry. No InitializeOnLoad, scene mutation or AutoRunPlayer.
        public static void BuildWindowsPlayer()
        {
            ValidatePinnedHost();
            var command = PsoCommandLine.Current;
            string output = command.GetString("-pso-native-build-output", string.Empty);
            if (string.IsNullOrWhiteSpace(output)) throw new BuildFailedException("A fresh build output path is required.");
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64) ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64).SequenceEqual(new[] { GraphicsDeviceType.Direct3D12 }))
                throw new BuildFailedException("Declare a D3D12 target cell for every arm before building; no silent API override.");
            if (System.IO.File.Exists(output) || System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output))))
                throw new BuildFailedException("Use a fresh build output directory; existing Player evidence is retained.");
            var options = new BuildPlayerOptions { scenes = scenes, locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                // Both training/final use the same options; training changes only the plan gate.
                options = BuildOptions.Development };
            PsoBuildIdentityCapture.DeclareBuildInputs(options);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Native host build failed: " + report.summary.result);
        }

        private static void ValidatePinnedHost()
        {
            if (Application.unityVersion != "6000.1.0f1")
                throw new BuildFailedException("Native host source lock requires Unity 6000.1.0f1; lock a separate cell before upgrading.");
            if (!EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
                .SequenceEqual(new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Main.unity" }))
                throw new BuildFailedException("Preserve the pinned upstream Menu/Main build scene order.");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.IL2CPP)
                throw new BuildFailedException("Preserve the pinned upstream Standalone IL2CPP backend; do not substitute Mono.");
        }
    }
}
