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
        // Invoked explicitly in a future authorized build. No InitializeOnLoad, scene mutation or AutoRunPlayer.
        public static void BuildWindowsPlayer()
        {
            var command = PsoCommandLine.Current;
            if (!command.HasFlag("-pso-native-build"))
                throw new BuildFailedException("Native host build is opt-in: requires -pso-native-build.");
            if (Application.unityVersion != "6000.1.0f1")
                throw new BuildFailedException("Native host source lock requires Unity 6000.1.0f1; lock a separate cell before upgrading.");
            string output = command.GetString("-pso-native-build-output", string.Empty);
            if (string.IsNullOrWhiteSpace(output)) throw new BuildFailedException("A fresh build output path is required.");
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (!scenes.SequenceEqual(new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Main.unity" }))
                throw new BuildFailedException("Preserve the pinned upstream Menu/Main build scene order.");
            if (!PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64).Contains(GraphicsDeviceType.Direct3D12))
                throw new BuildFailedException("Declare a D3D12 target cell for every arm before building; no silent API override.");
            if (System.IO.File.Exists(output) || System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output))))
                throw new BuildFailedException("Use a fresh build output directory; existing Player evidence is retained.");
            var options = new BuildPlayerOptions { scenes = scenes, locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                // Both training/final use the same options; training changes only the plan gate.
                options = BuildOptions.Development };
            PsoBuildIdentityCapture.DeclareBuildInputs(options);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Native host build failed: " + report.summary.result);
        }
    }
}
