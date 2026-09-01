using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PipelineEditor = Yanagisawa.ShaderHitchPipeline.Editor;

namespace Yanagisawa.ShaderHitchPipeline.Showcase.Editor
{
    public static class PsoShowcaseBuilder
    {
        private const string GeneratedDirectory =
            "Assets/ShaderHitchShowcaseGenerated";
        private const string ScenePath =
            GeneratedDirectory + "/PsoShowcase.unity";

        [MenuItem("Tools/Shader Hitch Pipeline/Build PSO Showcase")]
        public static void BuildWindowsPlayer()
        {
            string scene = CreateScene();
            ConfigureWindowsD3D12();

            string defaultOutput = Path.GetFullPath(
                "Builds/Windows/ShaderHitchShowcase.exe");
            string output = Path.GetFullPath(PsoCommandLine.Current.GetString(
                PsoConstants.BuildOutputArgument,
                defaultOutput));
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    "Showcase build failed: " + report.summary.result + ".");
            Debug.Log("[ShaderHitchPipeline] Showcase player: " + output);
        }

        public static string CreateScene()
        {
            Directory.CreateDirectory(GeneratedDirectory);
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var root = new GameObject("PSO Hitch Showcase");
            PsoShowcaseController controller = root.AddComponent<PsoShowcaseController>();

            string shaderGuid = AssetDatabase.FindAssets("PsoShowcase t:Shader")
                .OrderBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(shaderGuid))
                throw new InvalidOperationException("Could not locate PsoShowcase.shader.");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                AssetDatabase.GUIDToAssetPath(shaderGuid));
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("showcaseShader").objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        private static void ConfigureWindowsD3D12()
        {
            PlayerSettings.productName = "Shader Hitch Pipeline Showcase";
            PlayerSettings.companyName = "Yanagisawa";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D12 });

            PipelineEditor.PsoProjectConfiguration configuration =
                PipelineEditor.PsoProjectConfiguration.Load();
            configuration.targetRuntimePlatform = RuntimePlatform.WindowsPlayer.ToString();
            configuration.targetGraphicsDeviceType = GraphicsDeviceType.Direct3D12.ToString();
            configuration.Save();
        }
    }
}
