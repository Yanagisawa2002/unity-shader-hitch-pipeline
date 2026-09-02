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

namespace Yanagisawa.ShaderHitchPipeline.Showcase.Editor
{
    public static class PsoShowcaseBuilder
    {
        private const string GeneratedDirectory =
            "Assets/ShaderHitchShowcaseGenerated";
        private const string ScenePath =
            GeneratedDirectory + "/PsoShowcase.unity";
        private const string GeneratedShaderPath =
            GeneratedDirectory + "/PsoShowcaseGenerated.shader";
        private const string CacheBusterArgument =
            "-pso-showcase-cache-buster";
        private const string CacheBusterDefine =
            "#define PSO_SHOWCASE_CACHE_BUSTER 0u";

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

            Shader shader = GenerateCacheIsolatedShader();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("showcaseShader").objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        private static Shader GenerateCacheIsolatedShader()
        {
            string sourcePath = AssetDatabase.FindAssets("PsoShowcase t:Shader")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    !string.Equals(
                        path,
                        GeneratedShaderPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        Path.GetFileName(path),
                        "PsoShowcase.shader",
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.StartsWith("Assets/", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(sourcePath))
                throw new InvalidOperationException("Could not locate PsoShowcase.shader.");

            string cacheKey = PsoCommandLine.Current.GetString(
                CacheBusterArgument,
                "default");
            uint cacheBuster = StableHash(cacheKey);
            string source = File.ReadAllText(sourcePath);
            string generated = source
                .Replace(
                    "Shader \"Yanagisawa/Shader Hitch Showcase\"",
                    "Shader \"Hidden/Yanagisawa/Shader Hitch Showcase Generated\"")
                .Replace(
                    CacheBusterDefine,
                    "#define PSO_SHOWCASE_CACHE_BUSTER " + cacheBuster + "u");
            if (string.Equals(source, generated, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Showcase shader does not contain the cache-buster template tokens.");

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
                throw new InvalidOperationException("Generated showcase shader did not import.");

            Debug.Log("[ShaderHitchPipeline] Showcase shader cache key '" + cacheKey +
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
