using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yanagisawa.ShaderHitchPipeline.HotsetFixture
{
    public static class PsoHotsetFixtureBuilder
    {
        public static void Build()
        {
            Directory.CreateDirectory("Assets/HotsetFixtureGenerated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var fixture = new GameObject("Hotset routes").AddComponent<PsoHotsetFixturePlayer>();
            fixture.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/PsoHotsetFixture/Runtime/Hotset.shader");
            fixture.shaderSha256 = PsoFileUtility.ComputeSha256("Assets/PsoHotsetFixture/Runtime/Hotset.shader");
            if (fixture.shader == null) throw new Exception("Fixture shader missing");
            const string path = "Assets/HotsetFixtureGenerated/Hotset.unity";
            EditorSceneManager.SaveScene(scene, path);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 640;
            PlayerSettings.defaultScreenHeight = 360;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            string output = Path.GetFullPath(PsoCommandLine.Current.GetString("-hotset-player", "Builds/Hotset/Hotset.exe"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { path },
                locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Hotset fixture build failed");
        }
    }
}
