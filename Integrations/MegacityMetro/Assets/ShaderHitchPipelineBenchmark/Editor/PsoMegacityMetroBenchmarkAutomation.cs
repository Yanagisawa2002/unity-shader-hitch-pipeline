using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PipelineEditor = Yanagisawa.ShaderHitchPipeline.Editor;

namespace Yanagisawa.ShaderHitchPipeline.MegacityMetro.Editor
{
    /// <summary>
    /// Deterministic, command-line entry points for the pinned Megacity Metro
    /// validation project. The census is intentionally non-mutating: it lets the
    /// automation choose and audit a real renderer workload before binding it.
    /// </summary>
    public static class PsoMegacityMetroBenchmarkAutomation
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string LevelScenePath = "Assets/Scenes/Main/Level.unity";
        private const string PlayerInfoSettingsPath =
            "Assets/Settings/Gameplay/PlayerInfoItemSettings.asset";
        private const string RevealShaderPath =
            "Assets/ShaderHitchPipelineBenchmark/Runtime/MegacityPsoReveal.shader";
        private const string GeneratedDirectory =
            "Assets/ShaderHitchPipelineBenchmark/Generated";
        private const string GeneratedRevealShaderPath =
            GeneratedDirectory + "/MegacityPsoRevealGenerated.shader";
        private const string GeneratedRevealShaderName =
            "Hidden/Yanagisawa/Megacity Metro PSO Reveal Generated";
        private const string BenchmarkPhase = "megacity-metro-reveal";
        private const string CensusPathArgument = "-pso-megacity-census";
        private const string BindingPathArgument = "-pso-megacity-binding";
        private const string CacheBusterArgument = "-pso-megacity-cache-buster";
        private const string CacheBusterDefine =
            "#define MEGACITY_PSO_CACHE_BUSTER 0u";
        private const float SelectedNearMeters = 100.0f;
        private const float SelectedFarMeters = 800.0f;
        private const float SelectedHalfWidthMeters = 350.0f;
        private const int ControlledRevealLayer = 28;

        [Serializable]
        private sealed class CensusReport
        {
            public int schemaVersion = 1;
            public string generatedUtc;
            public string unityVersion;
            public string mainScene;
            public string levelScene;
            public CameraRecord[] cameras;
            public int rendererCount;
            public int activeRendererCount;
            public int materialSlotCount;
            public string[] uniqueShaders;
            public BoundsRecord levelBounds;
            public RootRecord[] roots;
            public CandidateRecord[] forwardCorridors;
        }

        [Serializable]
        private sealed class CameraRecord
        {
            public string name;
            public string scene;
            public bool enabled;
            public string tag;
            public Vector3 position;
            public Vector3 eulerAngles;
            public int cullingMask;
        }

        [Serializable]
        private sealed class RootRecord
        {
            public string name;
            public string hierarchyPath;
            public int rendererCount;
            public int activeRendererCount;
            public int materialSlotCount;
            public string[] uniqueShaders;
            public BoundsRecord bounds;
        }

        [Serializable]
        private sealed class BoundsRecord
        {
            public Vector3 center;
            public Vector3 size;
        }

        [Serializable]
        private sealed class CandidateRecord
        {
            public string name;
            public float nearMeters;
            public float farMeters;
            public float halfWidthMeters;
            public int rendererCount;
            public int materialSlotCount;
            public string[] uniqueShaders;
            public BoundsRecord bounds;
        }

        [Serializable]
        private sealed class BindingReceipt
        {
            public int schemaVersion = 1;
            public string generatedUtc;
            public string unityVersion;
            public string mainScene;
            public string levelScene;
            public string selection = "camera-forward-corridor";
            public float nearMeters;
            public float farMeters;
            public float halfWidthMeters;
            public int rendererCount;
            public int materialSlotCount;
            public string[] uniqueShaders;
            public BoundsRecord bounds;
            public CameraRecord camera;
            public bool singlePlayerModeForced;
            public string[] disabledNetworkRoots;
            public string[] disabledUiRoots;
            public int deferredGpuStateCount = 48;
            public string revealShader;
            public string cacheBusterKey;
            public string graphicsApi;
            public string qualityLevel;
            public string scriptingBackend;
            public bool mainSceneOnlyBuild;
            public int controlledRevealLayer;
            public bool contextRendererLayersStamped;
        }

        private sealed class Preparation
        {
            public Scene mainScene;
            public Scene levelScene;
            public Camera camera;
            public Renderer[] renderers;
            public Bounds bounds;
            public string[] disabledNetworkRoots;
            public string[] disabledUiRoots;
            public Shader revealShader;
            public string cacheBusterKey;
        }

        public static void CaptureCensus()
        {
            Scene mainScene = EditorSceneManager.OpenScene(
                MainScenePath,
                OpenSceneMode.Single);
            Scene levelScene = EditorSceneManager.OpenScene(
                LevelScenePath,
                OpenSceneMode.Additive);

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>()
                .Where(item => item != null &&
                               item.gameObject.scene.IsValid() &&
                               item.gameObject.scene.isLoaded)
                .OrderBy(item => item.gameObject.scene.path, StringComparer.Ordinal)
                .ThenBy(item => HierarchyPath(item.transform), StringComparer.Ordinal)
                .ToArray();
            Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>()
                .Where(item => item != null && item.gameObject.scene == levelScene)
                .OrderBy(item => HierarchyPath(item.transform), StringComparer.Ordinal)
                .ToArray();
            if (renderers.Length == 0)
                throw new InvalidOperationException("Megacity Level contains no authoring renderers.");

            Bounds levelBounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                levelBounds.Encapsulate(renderers[index].bounds);

            RootRecord[] roots = levelScene.GetRootGameObjects()
                .Select(root => CreateRootRecord(root))
                .Where(root => root.rendererCount > 0)
                .OrderByDescending(root => root.materialSlotCount)
                .ThenByDescending(root => root.rendererCount)
                .ThenBy(root => root.hierarchyPath, StringComparer.Ordinal)
                .ToArray();

            var report = new CensusReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                mainScene = mainScene.path,
                levelScene = levelScene.path,
                cameras = cameras.Select(item => new CameraRecord
                {
                    name = item.name,
                    scene = item.gameObject.scene.path,
                    enabled = item.enabled,
                    tag = item.tag,
                    position = item.transform.position,
                    eulerAngles = item.transform.eulerAngles,
                    cullingMask = item.cullingMask,
                }).ToArray(),
                rendererCount = renderers.Length,
                activeRendererCount = renderers.Count(IsActiveRenderer),
                materialSlotCount = renderers.Sum(MaterialSlotCount),
                uniqueShaders = UniqueShaders(renderers),
                levelBounds = ToRecord(levelBounds),
                roots = roots,
                forwardCorridors = cameras.Length == 0
                    ? Array.Empty<CandidateRecord>()
                    : new[]
                    {
                        CreateForwardCorridor(
                            "near-urban-block", cameras[0], renderers, 100.0f, 450.0f, 200.0f),
                        CreateForwardCorridor(
                            "medium-district", cameras[0], renderers, 100.0f, 800.0f, 350.0f),
                        CreateForwardCorridor(
                            "deep-flight-corridor", cameras[0], renderers, 100.0f, 1200.0f, 500.0f),
                    },
            };

            string outputPath = GetCommandLineValue(CensusPathArgument);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "../Temp/pso-megacity-census.json"));
            }
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            Debug.Log("[ShaderHitchPipeline] Megacity census saved to " + outputPath +
                      " (" + report.rendererCount + " renderers, " +
                      report.uniqueShaders.Length + " shaders, " +
                      report.roots.Length + " renderer roots)." );
        }

        [MenuItem("Tools/Shader Hitch Pipeline/Megacity/Prepare Deterministic Benchmark")]
        public static void PrepareBenchmark()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(RevealShaderPath);
            if (shader == null)
                throw new InvalidOperationException("Megacity PSO reveal shader is missing.");
            Preparation preparation = PrepareBenchmarkInternal(shader, "authoring");
            ConfigureWindowsPlayer();
            WriteBindingReceipt(preparation);
        }

        [MenuItem("Tools/Shader Hitch Pipeline/Megacity/Build Windows Benchmark")]
        public static void BuildWindowsPlayer()
        {
            string cacheBusterKey = PsoCommandLine.Current.GetString(
                CacheBusterArgument,
                "default");
            Shader shader = GenerateCacheIsolatedShader(cacheBusterKey);
            Preparation preparation = PrepareBenchmarkInternal(shader, cacheBusterKey);
            ConfigureWindowsPlayer();
            WriteBindingReceipt(preparation);

            string defaultOutput = Path.GetFullPath(
                "Builds/Windows/MegacityMetroPso/MegacityMetroPso.exe");
            string output = Path.GetFullPath(PsoCommandLine.Current.GetString(
                PsoConstants.BuildOutputArgument,
                defaultOutput));
            string outputDirectory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            bool trainingBuild = PsoCommandLine.Current.HasFlag(
                PsoConstants.TrainingBuildArgument);
            var options = new BuildPlayerOptions
            {
                // Enter the real gameplay scene directly. This keeps UGS/menu
                // startup outside the measured 12-second rendering window.
                scenes = new[] { MainScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = trainingBuild ? BuildOptions.Development : BuildOptions.None,
            };
            PipelineEditor.PsoBuildIdentityCapture.DeclareBuildInputs(options);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Megacity PSO benchmark build failed: " + report.summary.result + ".");
            }
            Debug.Log("[ShaderHitchPipeline] Megacity PSO player: " + output);
        }

        private static Preparation PrepareBenchmarkInternal(
            Shader revealShader,
            string cacheBusterKey)
        {
            Scene mainScene = EditorSceneManager.OpenScene(
                MainScenePath,
                OpenSceneMode.Single);
            Scene levelScene = EditorSceneManager.OpenScene(
                LevelScenePath,
                OpenSceneMode.Additive);
            Camera camera = FindBenchmarkCamera(mainScene);
            Renderer[] levelRenderers = Resources.FindObjectsOfTypeAll<Renderer>()
                .Where(item => item != null && item.gameObject.scene == levelScene)
                .ToArray();
            Renderer[] selected = SelectForwardCorridor(
                camera,
                levelRenderers,
                SelectedNearMeters,
                SelectedFarMeters,
                SelectedHalfWidthMeters);
            if (selected.Length < 100)
            {
                throw new InvalidOperationException(
                    "The pinned Megacity corridor unexpectedly contains only " +
                    selected.Length + " renderers.");
            }

            Bounds bounds = selected[0].bounds;
            for (int index = 1; index < selected.Length; index++)
                bounds.Encapsulate(selected[index].bounds);

            PsoMegacityMetroAdapterInstaller.BindRendererSet(
                selected,
                camera,
                false,
                revealShader,
                ControlledRevealLayer,
                false);
            ForceSinglePlayerMode();
            string[] disabledNetworkRoots = DisableNetworkRoots(mainScene);
            string[] disabledUiRoots = DisableNamedRoots(mainScene, "Tutorial");
            EditorSceneManager.SaveScene(levelScene);
            EditorSceneManager.SaveScene(mainScene);
            AssetDatabase.SaveAssets();

            return new Preparation
            {
                mainScene = mainScene,
                levelScene = levelScene,
                camera = camera,
                renderers = selected,
                bounds = bounds,
                disabledNetworkRoots = disabledNetworkRoots,
                disabledUiRoots = disabledUiRoots,
                revealShader = revealShader,
                cacheBusterKey = cacheBusterKey,
            };
        }

        private static Shader GenerateCacheIsolatedShader(string cacheBusterKey)
        {
            Directory.CreateDirectory(GeneratedDirectory);
            string source = File.ReadAllText(RevealShaderPath);
            uint cacheBuster = StableHash(cacheBusterKey);
            string generated = source
                .Replace(
                    "Shader \"Yanagisawa/Megacity Metro PSO Reveal\"",
                    "Shader \"Hidden/Yanagisawa/Megacity Metro PSO Reveal Generated\"")
                .Replace(
                    CacheBusterDefine,
                    "#define MEGACITY_PSO_CACHE_BUSTER " + cacheBuster + "u");
            if (string.Equals(source, generated, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Megacity reveal shader does not contain the cache-buster tokens.");
            }
            File.WriteAllText(
                GeneratedRevealShaderPath,
                generated,
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                GeneratedRevealShaderPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                GeneratedRevealShaderPath);
            if (shader == null)
                throw new InvalidOperationException("Generated Megacity reveal shader did not import.");
            Debug.Log("[ShaderHitchPipeline] Megacity shader cache key '" +
                      cacheBusterKey + "' -> " + cacheBuster + ".");
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

        private static Camera FindBenchmarkCamera(Scene mainScene)
        {
            Camera camera = Resources.FindObjectsOfTypeAll<Camera>()
                .Where(item => item != null && item.gameObject.scene == mainScene)
                .OrderByDescending(item => item.CompareTag("MainCamera"))
                .ThenByDescending(item => item.enabled)
                .ThenBy(item => HierarchyPath(item.transform), StringComparer.Ordinal)
                .FirstOrDefault();
            if (camera == null)
                throw new InvalidOperationException("The pinned Megacity Main scene has no camera.");
            return camera;
        }

        private static Renderer[] SelectForwardCorridor(
            Camera camera,
            IEnumerable<Renderer> source,
            float nearMeters,
            float farMeters,
            float halfWidthMeters)
        {
            Vector3 origin = camera.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return source.Where(item =>
                {
                    Vector3 offset = item.bounds.center - origin;
                    float longitudinal = Vector3.Dot(offset, forward);
                    float lateral = Mathf.Abs(Vector3.Dot(offset, right));
                    return IsActiveRenderer(item) &&
                           longitudinal >= nearMeters && longitudinal <= farMeters &&
                           lateral <= halfWidthMeters;
                })
                .OrderBy(item => HierarchyPath(item.transform), StringComparer.Ordinal)
                .ToArray();
        }

        private static void ForceSinglePlayerMode()
        {
            ScriptableObject settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                PlayerInfoSettingsPath);
            if (settings == null)
                throw new InvalidOperationException("Megacity PlayerInfoItemSettings is missing.");
            var serialized = new SerializedObject(settings);
            SerializedProperty mode = serialized.FindProperty("GameMode");
            if (mode == null)
                throw new InvalidOperationException("Megacity GameMode field is missing.");
            // Unity.MegacityMetro.Gameplay.GameMode.SinglePlayer == 1 at the pin.
            mode.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static string[] DisableNetworkRoots(Scene mainScene)
        {
            return DisableNamedRoots(mainScene, "MatchmakingConnector");
        }

        private static string[] DisableNamedRoots(Scene mainScene, params string[] names)
        {
            var disabled = new List<string>();
            foreach (GameObject root in mainScene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!names.Contains(transform.name, StringComparer.Ordinal))
                        continue;
                    disabled.Add(HierarchyPath(transform));
                    if (transform.gameObject.activeSelf)
                    {
                        transform.gameObject.SetActive(false);
                        EditorUtility.SetDirty(transform.gameObject);
                        EditorSceneManager.MarkSceneDirty(mainScene);
                    }
                }
            }
            return disabled.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        }

        private static void ConfigureWindowsPlayer()
        {
            PlayerSettings.productName = "Megacity Metro - Shader Hitch Pipeline";
            PlayerSettings.companyName = "Yanagisawa";
            PlayerSettings.runInBackground = true;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D12 });
            // The exact editor installer includes the Windows Mono player. The
            // rendering comparison keeps this backend identical across all arms;
            // shader/PSO behavior remains in the native graphics stack.
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x);

            PipelineEditor.PsoProjectConfiguration configuration =
                PipelineEditor.PsoProjectConfiguration.Load();
            configuration.targetRuntimePlatform = RuntimePlatform.WindowsPlayer.ToString();
            configuration.targetGraphicsDeviceType = GraphicsDeviceType.Direct3D12.ToString();
            int mediumQuality = Array.FindIndex(
                QualitySettings.names,
                item => string.Equals(item, "Medium", StringComparison.Ordinal));
            if (mediumQuality < 0)
                throw new InvalidOperationException("Megacity Medium quality level was not found.");
            QualitySettings.SetQualityLevel(mediumQuality, true);
            configuration.targetQualityLevelName = QualitySettings.names[mediumQuality];
            configuration.targetFrameMilliseconds = 16.67;
            configuration.deferredDeadlineMilliseconds = 2500.0;
            configuration.deferredExpectedUseProbability = 1.0;
            configuration.deferredHotSetTier = 1;
            configuration.preinteractiveBootstrap = true;
            configuration.excludedPhases = new[] { "startup" };
            configuration.phaseShaderFilters = new[]
            {
                new PipelineEditor.PsoPhaseShaderFilter
                {
                    phase = BenchmarkPhase,
                    shaderNames = new[] { GeneratedRevealShaderName },
                },
            };
            configuration.Save();
        }

        private static void WriteBindingReceipt(Preparation preparation)
        {
            string outputPath = GetCommandLineValue(BindingPathArgument);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.GetFullPath(
                    "PsoArtifacts/MegacityBinding/binding.json");
            }
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            Camera camera = preparation.camera;
            var receipt = new BindingReceipt
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                mainScene = preparation.mainScene.path,
                levelScene = preparation.levelScene.path,
                nearMeters = SelectedNearMeters,
                farMeters = SelectedFarMeters,
                halfWidthMeters = SelectedHalfWidthMeters,
                rendererCount = preparation.renderers.Length,
                materialSlotCount = preparation.renderers.Sum(MaterialSlotCount),
                uniqueShaders = UniqueShaders(preparation.renderers),
                revealShader = AssetDatabase.GetAssetPath(preparation.revealShader),
                cacheBusterKey = preparation.cacheBusterKey,
                bounds = ToRecord(preparation.bounds),
                camera = new CameraRecord
                {
                    name = camera.name,
                    scene = camera.gameObject.scene.path,
                    enabled = camera.enabled,
                    tag = camera.tag,
                    position = camera.transform.position,
                    eulerAngles = camera.transform.eulerAngles,
                    cullingMask = camera.cullingMask,
                },
                singlePlayerModeForced = true,
                disabledNetworkRoots = preparation.disabledNetworkRoots,
                disabledUiRoots = preparation.disabledUiRoots,
                graphicsApi = GraphicsDeviceType.Direct3D12.ToString(),
                qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                scriptingBackend = ScriptingImplementation.Mono2x.ToString(),
                mainSceneOnlyBuild = true,
                controlledRevealLayer = ControlledRevealLayer,
                contextRendererLayersStamped = false,
            };
            File.WriteAllText(outputPath, JsonUtility.ToJson(receipt, true));
            Debug.Log("[ShaderHitchPipeline] Megacity binding receipt: " + outputPath);
        }

        private static RootRecord CreateRootRecord(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new RootRecord
                {
                    name = root.name,
                    hierarchyPath = HierarchyPath(root.transform),
                    uniqueShaders = Array.Empty<string>(),
                };
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return new RootRecord
            {
                name = root.name,
                hierarchyPath = HierarchyPath(root.transform),
                rendererCount = renderers.Length,
                activeRendererCount = renderers.Count(IsActiveRenderer),
                materialSlotCount = renderers.Sum(MaterialSlotCount),
                uniqueShaders = UniqueShaders(renderers),
                bounds = ToRecord(bounds),
            };
        }

        private static CandidateRecord CreateForwardCorridor(
            string name,
            Camera camera,
            IEnumerable<Renderer> source,
            float nearMeters,
            float farMeters,
            float halfWidthMeters)
        {
            Vector3 origin = camera.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Renderer[] renderers = source.Where(item =>
            {
                Vector3 offset = item.bounds.center - origin;
                float longitudinal = Vector3.Dot(offset, forward);
                float lateral = Mathf.Abs(Vector3.Dot(offset, right));
                return IsActiveRenderer(item) &&
                       longitudinal >= nearMeters && longitudinal <= farMeters &&
                       lateral <= halfWidthMeters;
            }).ToArray();

            BoundsRecord bounds = null;
            if (renderers.Length > 0)
            {
                Bounds aggregate = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++)
                    aggregate.Encapsulate(renderers[index].bounds);
                bounds = ToRecord(aggregate);
            }
            return new CandidateRecord
            {
                name = name,
                nearMeters = nearMeters,
                farMeters = farMeters,
                halfWidthMeters = halfWidthMeters,
                rendererCount = renderers.Length,
                materialSlotCount = renderers.Sum(MaterialSlotCount),
                uniqueShaders = UniqueShaders(renderers),
                bounds = bounds,
            };
        }

        private static bool IsActiveRenderer(Renderer renderer)
        {
            return renderer.enabled && renderer.gameObject.activeInHierarchy;
        }

        private static int MaterialSlotCount(Renderer renderer)
        {
            return renderer.sharedMaterials == null ? 0 : renderer.sharedMaterials.Length;
        }

        private static string[] UniqueShaders(IEnumerable<Renderer> renderers)
        {
            return renderers
                .SelectMany(item => item.sharedMaterials ?? Array.Empty<Material>())
                .Where(item => item != null && item.shader != null)
                .Select(item => item.shader.name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
        }

        private static BoundsRecord ToRecord(Bounds bounds)
        {
            return new BoundsRecord { center = bounds.center, size = bounds.size };
        }

        private static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }

        private static string GetCommandLineValue(string key)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], key, StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1];
            }
            return string.Empty;
        }
    }
}
