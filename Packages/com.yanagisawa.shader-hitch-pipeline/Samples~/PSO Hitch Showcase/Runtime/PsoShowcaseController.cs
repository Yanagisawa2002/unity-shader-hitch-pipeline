using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yanagisawa.ShaderHitchPipeline.Showcase
{
    [DefaultExecutionOrder(-1000)]
    public sealed class PsoShowcaseController : MonoBehaviour
    {
        private const int Columns = 24;
        private const int Rows = 16;
        private const int HistoryLength = 180;
        private const int RevealBatchSize = 8;
        private const double RevealIntervalSeconds = 1.0 / 60.0;
        private const string VisualMarkerArgument = "-pso-showcase-marker";

        private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly float[] frameHistory = new float[HistoryLength];
        private Texture2D white;
        private int historyCursor;
        private int visibleCount;
        private double readyAt;
        private double nextRevealAt;
        private bool sequenceStarted;
        private string mode;
        private string visualMarkerFile;

        [SerializeField]
        private Shader showcaseShader;

        private void Awake()
        {
            Application.targetFrameRate = 240;
            QualitySettings.vSyncCount = 0;
            visualMarkerFile = PsoCommandLine.Current.GetString(
                VisualMarkerArgument,
                string.Empty);
            mode = PsoCommandLine.Current.HasFlag(PsoConstants.DisableWarmupArgument)
                ? "BASELINE / COLD"
                : "OPTIMIZED / PREWARMED";
            SetupCamera();
            BuildTiles();
            white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            double revealDelaySeconds = PsoCommandLine.Current.GetDouble(
                PsoConstants.BenchmarkDelayArgument,
                1.0,
                0.0,
                3600.0);
            readyAt = PsoCommandLine.Current.HasFlag(PsoConstants.DisableWarmupArgument)
                ? Time.realtimeSinceStartupAsDouble + revealDelaySeconds
                : -1.0;
            EmitVisualMarker("CAPTURE_READY", Time.realtimeSinceStartupAsDouble);
        }

        private void Update()
        {
            frameHistory[historyCursor] = Time.unscaledDeltaTime * 1000.0f;
            historyCursor = (historyCursor + 1) % frameHistory.Length;

            if (!sequenceStarted)
            {
                if (!WarmupReady())
                    return;
                if (readyAt < 0.0)
                {
                    double revealDelaySeconds = PsoCommandLine.Current.GetDouble(
                        PsoConstants.BenchmarkDelayArgument,
                        1.0,
                        0.0,
                        3600.0);
                    readyAt = Time.realtimeSinceStartupAsDouble + revealDelaySeconds;
                }
                if (Time.realtimeSinceStartupAsDouble < readyAt)
                    return;
                sequenceStarted = true;
                nextRevealAt = Time.realtimeSinceStartupAsDouble;
                EmitVisualMarker("WORKLOAD_START", nextRevealAt);
            }

            if (renderers.Count == 0 || visibleCount >= renderers.Count)
                return;

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextRevealAt)
                return;

            // Pace first use at 60 reveal batches per second. A cold hitch delays the next
            // batch instead of catching up, so an ordinary 60 FPS recording shows the real
            // presentation freeze while both modes still execute the identical workload.
            nextRevealAt = now + RevealIntervalSeconds;
            int batchCount = Math.Min(RevealBatchSize, renderers.Count - visibleCount);
            for (int count = 0; count < batchCount; count++)
            {
                int index = visibleCount;
                renderers[index].enabled = true;
                visibleCount++;
            }
        }

        private bool WarmupReady()
        {
            if (PsoCommandLine.Current.HasFlag(PsoConstants.DisableWarmupArgument))
                return true;
            PsoWarmupOrchestrator orchestrator = PsoWarmupOrchestrator.Instance;
            return orchestrator == null || orchestrator.IsComplete;
        }

        private void SetupCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }
            camera.orthographic = true;
            camera.orthographicSize = 4.6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.032f, 0.055f, 1.0f);
            camera.transform.position = new Vector3(0.0f, 0.0f, -10.0f);
            camera.transform.rotation = Quaternion.identity;
        }

        private void BuildTiles()
        {
            Shader shader = showcaseShader != null
                ? showcaseShader
                : Shader.Find("Yanagisawa/Shader Hitch Showcase");
            if (shader == null)
                throw new InvalidOperationException("Showcase shader was not included in the build.");

            string[] keywords =
            {
                "PSO_RIM", "PSO_PATTERN", "PSO_EMISSION", "PSO_CLIP", "PSO_WARP",
                "PSO_NOISE", "PSO_FRESNEL2", "PSO_GRADIENT", "PSO_CAPTURE_V2",
            };
            for (int index = 0; index < Columns * Rows; index++)
            {
                var material = new Material(shader)
                {
                    name = "PSO Combination " + index,
                    renderQueue = 3000,
                };
                for (int bit = 0; bit < keywords.Length; bit++)
                {
                    if ((index & (1 << bit)) != 0)
                        material.EnableKeyword(keywords[bit]);
                }

                material.SetColor(
                    "_BaseColor",
                    Color.HSVToRGB(index / (float)(Columns * Rows), 0.72f, 0.95f));
                material.SetInt("_SrcBlend", (int)(index % 3 == 0
                    ? BlendMode.SrcAlpha
                    : index % 3 == 1 ? BlendMode.One : BlendMode.DstColor));
                material.SetInt("_DstBlend", (int)(index % 3 == 0
                    ? BlendMode.OneMinusSrcAlpha
                    : index % 3 == 1 ? BlendMode.One : BlendMode.Zero));
                material.SetInt("_ZWrite", index % 2);
                material.SetInt("_Cull", (int)(index % 3 == 0
                    ? CullMode.Off
                    : index % 3 == 1 ? CullMode.Back : CullMode.Front));
                material.SetInt("_ZTest", (int)(index % 4 == 0
                    ? CompareFunction.Always
                    : CompareFunction.LessEqual));
                materials.Add(material);

                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tile.name = "Combination " + index;
                tile.transform.SetParent(transform, false);
                int column = index % Columns;
                int row = index / Columns;
                tile.transform.localPosition = new Vector3(
                    (column - ((Columns - 1) * 0.5f)) * 0.32f,
                    (((Rows - 1) * 0.5f) - row) * 0.32f - 0.45f,
                    (index % 7) * 0.002f);
                tile.transform.localScale = Vector3.one * 0.27f;
                Collider collider = tile.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
                MeshRenderer renderer = tile.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.enabled = false;
                renderers.Add(renderer);
            }
        }

        private void OnGUI()
        {
            if (white == null)
                return;

            GUIStyle title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.88f, 0.94f, 1.0f) },
            };
            GUIStyle small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.62f, 0.76f, 0.90f) },
            };
            GUI.Label(new Rect(24, 15, 810, 34), "SHADER HITCH PIPELINE · " + mode, title);
            GUI.Label(new Rect(25, 48, 700, 24),
                (Columns * Rows) + " shader/PSO combinations · first-use frame time · " +
                SystemInfo.graphicsDeviceType,
                small);

            GUIStyle status = new GUIStyle(small)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperRight,
                normal = { textColor = new Color(0.42f, 0.92f, 0.72f) },
            };
            string statusText;
            if (!sequenceStarted)
            {
                statusText = readyAt < 0.0
                    ? "PREWARMING CAPTURED STATES…"
                    : "WORKLOAD IN " + Math.Max(0.0,
                        readyAt - Time.realtimeSinceStartupAsDouble).ToString(
                            "F1",
                            CultureInfo.InvariantCulture) + " s";
            }
            else if (visibleCount < renderers.Count)
            {
                statusText = "LIVE FIRST USE";
            }
            else
            {
                statusText = "384 / 384 COMPLETE";
            }
            GUI.Label(new Rect(Screen.width - 430, 19, 400, 30), statusText, status);

            Rect graph = new Rect(24, Screen.height - 164, Screen.width - 48, 130);
            DrawRect(graph, new Color(0.035f, 0.050f, 0.078f, 0.94f));
            float thresholdY = graph.yMax - Mathf.Clamp01(8.33f / 24.0f) * graph.height;
            DrawRect(new Rect(graph.x, thresholdY, graph.width, 1), new Color(1.0f, 0.35f, 0.32f, 0.7f));
            for (int offset = 0; offset < frameHistory.Length; offset++)
            {
                int sampleIndex = (historyCursor + offset) % frameHistory.Length;
                float milliseconds = frameHistory[sampleIndex];
                float height = Mathf.Clamp01(milliseconds / 24.0f) * graph.height;
                float x = graph.x + (offset * graph.width / frameHistory.Length);
                Color color = milliseconds >= 8.33f
                    ? new Color(1.0f, 0.25f, 0.22f, 0.95f)
                    : new Color(0.18f, 0.78f, 1.0f, 0.90f);
                DrawRect(new Rect(x, graph.yMax - height, Mathf.Max(1, graph.width / frameHistory.Length), height), color);
            }
            GUI.Label(new Rect(graph.x + 8, graph.y + 5, graph.width - 16, 22),
                "Frame time (red = ≥ 8.33 ms / missed 120 FPS) · combinations revealed: " +
                Mathf.Min(visibleCount, renderers.Count) + "/" + renderers.Count,
                small);
        }

        private void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        private void EmitVisualMarker(string marker, double realtimeSeconds)
        {
            string line = marker + " realtime=" +
                          realtimeSeconds.ToString("F6", CultureInfo.InvariantCulture);
            Debug.Log("[ShaderHitchPipeline.Showcase] " + line);
            if (string.IsNullOrWhiteSpace(visualMarkerFile))
                return;

            try
            {
                string fullPath = Path.GetFullPath(visualMarkerFile);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(
                    fullPath,
                    line + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[ShaderHitchPipeline.Showcase] Could not write visual marker: " +
                    exception.Message);
            }
        }

        private void OnDestroy()
        {
            for (int index = 0; index < materials.Count; index++)
                Destroy(materials[index]);
            if (white != null)
                Destroy(white);
        }
    }
}
