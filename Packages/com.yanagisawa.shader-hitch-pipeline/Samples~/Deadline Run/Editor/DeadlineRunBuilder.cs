using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PipelineEditor = Yanagisawa.ShaderHitchPipeline.Editor;

namespace Yanagisawa.ShaderHitchPipeline.DeadlineRun.Editor
{
    public static class DeadlineRunBuilder
    {
        private const string GeneratedDirectory = "Assets/DeadlineRunGenerated";
        private const string ScenePath = GeneratedDirectory + "/DeadlineRun.unity";
        private const string GeneratedShaderPath =
            GeneratedDirectory + "/DeadlineRunGenerated.shader";
        private const string CacheBusterArgument = "-pso-scenario-cache-buster";
        private const string CacheBusterDefine = "#define DEADLINE_RUN_CACHE_BUSTER 0u";

        [MenuItem("Tools/Shader Hitch Pipeline/Build Deadline Run")]
        public static void BuildWindowsPlayer()
        {
            string scene = CreateScene();
            ConfigureWindowsD3D12();
            bool trainingBuild = PsoCommandLine.Current.HasFlag(
                PsoConstants.TrainingBuildArgument);

            string defaultOutput = Path.GetFullPath("Builds/Windows/DeadlineRun.exe");
            string output = Path.GetFullPath(PsoCommandLine.Current.GetString(
                PsoConstants.BuildOutputArgument,
                defaultOutput));
            string directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                // GraphicsStateCollection tracing is only populated by the
                // Development Player on the tested Unity/Windows path. All footage
                // and both measured A/B Players remain release builds so the player
                // connection cannot obscure the benchmark with a firewall dialog.
                options = trainingBuild
                    ? BuildOptions.Development
                    : BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Deadline Run build failed: " + report.summary.result + ".");
            }
            Debug.Log("[ShaderHitchPipeline] Deadline Run player: " + output);
        }

        public static string CreateScene()
        {
            Directory.CreateDirectory(GeneratedDirectory);
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var root = new GameObject("Deadline Run");
            DeadlineRunController controller = root.AddComponent<DeadlineRunController>();

            Shader shader = GenerateCacheIsolatedShader();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("deadlineRunShader").objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        private static Shader GenerateCacheIsolatedShader()
        {
            string sourcePath = AssetDatabase.FindAssets("DeadlineRun t:Shader")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    !string.Equals(
                        path,
                        GeneratedShaderPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        Path.GetFileName(path),
                        "DeadlineRun.shader",
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.StartsWith("Assets/", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(sourcePath))
                throw new InvalidOperationException("Could not locate DeadlineRun.shader.");

            string cacheKey = PsoCommandLine.Current.GetString(
                CacheBusterArgument,
                "default");
            uint cacheBuster = StableHash(cacheKey);
            string source = File.ReadAllText(sourcePath);
            string generated = source
                .Replace(
                    "Shader \"Yanagisawa/Deadline Run\"",
                    "Shader \"Hidden/Yanagisawa/Deadline Run Generated\"")
                .Replace(
                    CacheBusterDefine,
                    "#define DEADLINE_RUN_CACHE_BUSTER " + cacheBuster + "u");
            if (string.Equals(source, generated, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Deadline Run shader does not contain the cache-buster template tokens.");
            }

            File.WriteAllText(
                GeneratedShaderPath,
                generated,
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                GeneratedShaderPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(GeneratedShaderPath);
            if (shader == null)
                throw new InvalidOperationException("Generated Deadline Run shader did not import.");

            Debug.Log("[ShaderHitchPipeline] Deadline Run shader cache key '" + cacheKey +
                      "' -> " + cacheBuster + ".");
            return shader;
        }

        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            string text = value ?? string.Empty;
            for (int index = 0; index < text.Length; index++)
            {
                hash ^= text[index];
                hash *= 16777619u;
            }
            return hash == 0u ? 1u : hash;
        }

        private static void ConfigureWindowsD3D12()
        {
            PlayerSettings.productName = "Deadline Run - Shader Hitch Pipeline";
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
            configuration.targetFrameMilliseconds = 16.67;
            configuration.deferredDeadlineMilliseconds =
                DeadlineRunController.DeferredDeadlineSeconds * 1000.0;
            configuration.deferredExpectedUseProbability = 1.0;
            configuration.deferredHotSetTier = 1;
            configuration.preinteractiveBootstrap = true;
            configuration.Save();
        }
    }
}
