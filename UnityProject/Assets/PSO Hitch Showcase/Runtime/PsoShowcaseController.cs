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
        private const float HitchThresholdMilliseconds = 8.33f;
        private const double RevealIntervalSeconds = 1.0 / 60.0;
        private const string VisualMarkerArgument = "-pso-showcase-marker";

        private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly float[] frameHistory = new float[HistoryLength];
        private Texture2D white;
        private int historyCursor;
        private int historyCount;
        private int visibleCount;
        private int hitchFrameCount;
        private double readyAt;
        private double nextRevealAt;
        private double presentationEpoch;
        private double measurementStartedAt;
        private double missedPresentationMilliseconds;
        private float currentFrameMilliseconds;
        private float worstFrameMilliseconds;
        private bool sequenceStarted;
        private string mode;
        private Color modeColor;
        private string visualMarkerFile;

        [SerializeField]
        private Shader showcaseShader;

        private void Awake()
        {
            Application.targetFrameRate = 240;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            presentationEpoch = Time.realtimeSinceStartupAsDouble;
            visualMarkerFile = PsoCommandLine.Current.GetString(
                VisualMarkerArgument,
                string.Empty);
            ConfigureMode();
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

        private void ConfigureMode()
        {
            string requested = PsoCommandLine.Current.GetString(
                PsoConstants.BenchmarkModeArgument,
                PsoCommandLine.Current.HasFlag(PsoConstants.DisableWarmupArgument)
                    ? "baseline"
                    : "scheduled");
            if (PsoCommandLine.Current.HasFlag(PsoConstants.DisableWarmupArgument) ||
                string.Equals(requested, "baseline", StringComparison.OrdinalIgnoreCase))
            {
                mode = "COLD / NO WARMUP";
                modeColor = new Color(1.0f, 0.34f, 0.30f);
            }
            else if (string.Equals(requested, "naive", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(requested, "throughput", StringComparison.OrdinalIgnoreCase))
            {
                mode = "UNITY / ALL-AT-ONCE";
                modeColor = new Color(1.0f, 0.72f, 0.24f);
            }
            else
            {
                mode = "OURS / DEADLINE SCHEDULED";
                modeColor = new Color(0.32f, 0.92f, 0.68f);
            }
        }

        private void Update()
        {
            if (sequenceStarted)
                RecordMeasuredFrame();

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
                BeginMeasuredWorkload();
            }

            if (renderers.Count == 0 || visibleCount >= renderers.Count)
                return;

            // Pace first use at 60 reveal batches per second. A cold hitch delays the next
            // batch instead of catching up, so an ordinary 60 FPS recording shows the real
            // presentation freeze while every mode still executes the identical workload.
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextRevealAt)
                return;
            nextRevealAt = now + RevealIntervalSeconds;
            int batchCount = Math.Min(RevealBatchSize, renderers.Count - visibleCount);
            for (int count = 0; count < batchCount; count++)
            {
                renderers[visibleCount].enabled = true;
                visibleCount++;
            }
        }

        private void BeginMeasuredWorkload()
        {
            sequenceStarted = true;
            measurementStartedAt = Time.realtimeSinceStartupAsDouble;
            presentationEpoch = measurementStartedAt;
            nextRevealAt = measurementStartedAt;
            Array.Clear(frameHistory, 0, frameHistory.Length);
            historyCursor = 0;
            historyCount = 0;
            currentFrameMilliseconds = 0.0f;
            worstFrameMilliseconds = 0.0f;
            missedPresentationMilliseconds = 0.0;
            hitchFrameCount = 0;
            EmitVisualMarker("WORKLOAD_START", measurementStartedAt);
        }

        private void RecordMeasuredFrame()
        {
            currentFrameMilliseconds = Time.unscaledDeltaTime * 1000.0f;
            frameHistory[historyCursor] = currentFrameMilliseconds;
            historyCursor = (historyCursor + 1) % frameHistory.Length;
            historyCount = Math.Min(HistoryLength, historyCount + 1);
            worstFrameMilliseconds = Mathf.Max(
                worstFrameMilliseconds,
                currentFrameMilliseconds);
            if (currentFrameMilliseconds >= HitchThresholdMilliseconds)
            {
                hitchFrameCount++;
                missedPresentationMilliseconds +=
                    currentFrameMilliseconds - HitchThresholdMilliseconds;
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
            GUIStyle status = new GUIStyle(small)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperRight,
                normal = { textColor = modeColor },
            };

            DrawPresentationMotion(small);
            GUI.Label(new Rect(24, 15, 780, 34), "SHADER HITCH PIPELINE · " + mode, title);
            GUI.Label(new Rect(25, 48, 760, 24),
                (Columns * Rows) + " real shader/graphics-state combinations · " +
                SystemInfo.graphicsDeviceType,
                small);
            GUI.Label(new Rect(Screen.width - 450, 19, 420, 30), StatusText(), status);

            DrawMetricCard(
                new Rect(24, 78, 150, 56),
                "CURRENT",
                currentFrameMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + " ms",
                small,
                currentFrameMilliseconds >= HitchThresholdMilliseconds
                    ? new Color(1.0f, 0.32f, 0.28f)
                    : new Color(0.35f, 0.86f, 1.0f));
            DrawMetricCard(
                new Rect(184, 78, 150, 56),
                "WORST",
                worstFrameMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + " ms",
                small,
                worstFrameMilliseconds >= HitchThresholdMilliseconds
                    ? new Color(1.0f, 0.32f, 0.28f)
                    : new Color(0.35f, 0.86f, 1.0f));
            DrawMetricCard(
                new Rect(344, 78, 192, 56),
                "MISSED PRESENTATION",
                missedPresentationMilliseconds.ToString("F1", CultureInfo.InvariantCulture) +
                " ms · " + hitchFrameCount + " frames",
                small,
                hitchFrameCount > 0
                    ? new Color(1.0f, 0.54f, 0.30f)
                    : new Color(0.32f, 0.92f, 0.68f));

            Rect graph = new Rect(24, Screen.height - 164, Screen.width - 48, 130);
            DrawRect(graph, new Color(0.035f, 0.050f, 0.078f, 0.94f));
            float thresholdY = graph.yMax -
                               Mathf.Clamp01(HitchThresholdMilliseconds / 24.0f) *
                               graph.height;
            DrawRect(
                new Rect(graph.x, thresholdY, graph.width, 1),
                new Color(1.0f, 0.35f, 0.32f, 0.7f));
            int firstSample = (historyCursor - historyCount + HistoryLength) % HistoryLength;
            for (int offset = 0; offset < historyCount; offset++)
            {
                int sampleIndex = (firstSample + offset) % HistoryLength;
                float milliseconds = frameHistory[sampleIndex];
                float height = Mathf.Clamp01(milliseconds / 24.0f) * graph.height;
                float x = graph.x + (offset * graph.width / HistoryLength);
                Color color = milliseconds >= HitchThresholdMilliseconds
                    ? new Color(1.0f, 0.25f, 0.22f, 0.95f)
                    : new Color(0.18f, 0.78f, 1.0f, 0.90f);
                DrawRect(
                    new Rect(
                        x,
                        graph.yMax - height,
                        Mathf.Max(1, graph.width / HistoryLength),
                        height),
                    color);
            }
            string measurementLabel = sequenceStarted
                ? "Measurement reset at WORKLOAD_START · red = ≥ 8.33 ms / missed 120 FPS"
                : "Measurement arms at WORKLOAD_START · presentation clock remains live";
            GUI.Label(
                new Rect(graph.x + 8, graph.y + 5, graph.width - 16, 22),
                measurementLabel + " · states: " +
                Mathf.Min(visibleCount, renderers.Count) + "/" + renderers.Count,
                small);
        }

        private string StatusText()
        {
            if (!sequenceStarted)
            {
                return readyAt < 0.0
                    ? "PREWARMING CAPTURED STATES…"
                    : "WORKLOAD IN " + Math.Max(
                        0.0,
                        readyAt - Time.realtimeSinceStartupAsDouble).ToString(
                            "F1",
                            CultureInfo.InvariantCulture) + " s";
            }
            if (visibleCount < renderers.Count)
                return "LIVE FIRST USE · T+" +
                       (Time.realtimeSinceStartupAsDouble - measurementStartedAt).ToString(
                           "F2",
                           CultureInfo.InvariantCulture) + " s";
            return "384 / 384 COMPLETE";
        }

        private void DrawPresentationMotion(GUIStyle style)
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - presentationEpoch;
            float phase = (float)((elapsed * 0.34) % 1.0);
            float x = 24.0f + phase * (Screen.width - 48.0f);
            DrawRect(
                new Rect(x - 5.0f, 142.0f, 11.0f, Screen.height - 318.0f),
                new Color(0.15f, 0.85f, 1.0f, 0.08f));
            DrawRect(
                new Rect(x - 1.0f, 142.0f, 3.0f, Screen.height - 318.0f),
                new Color(0.38f, 0.94f, 1.0f, 0.78f));

            Vector2 center = new Vector2(Screen.width - 83.0f, 103.0f);
            DrawRect(
                new Rect(center.x - 39.0f, center.y - 28.0f, 78.0f, 56.0f),
                new Color(0.035f, 0.050f, 0.078f, 0.92f));
            float angle = (float)((elapsed * 360.0) % 360.0) - 90.0f;
            float radians = angle * Mathf.Deg2Rad;
            Vector2 hand = center + new Vector2(
                Mathf.Cos(radians) * 19.0f,
                Mathf.Sin(radians) * 19.0f);
            DrawLine(center, hand, 3.0f, modeColor);
            DrawRect(
                new Rect(center.x - 2.0f, center.y - 2.0f, 4.0f, 4.0f),
                Color.white);
            GUIStyle clock = new GUIStyle(style)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                normal = { textColor = modeColor },
            };
            GUI.Label(
                new Rect(center.x - 150.0f, center.y + 28.0f, 186.0f, 22.0f),
                "PRESENT T+" + elapsed.ToString("F3", CultureInfo.InvariantCulture),
                clock);
        }

        private void DrawMetricCard(
            Rect rect,
            string label,
            string value,
            GUIStyle baseStyle,
            Color valueColor)
        {
            DrawRect(rect, new Color(0.035f, 0.050f, 0.078f, 0.90f));
            GUIStyle labelStyle = new GUIStyle(baseStyle)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.52f, 0.67f, 0.80f) },
            };
            GUIStyle valueStyle = new GUIStyle(baseStyle)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = valueColor },
            };
            GUI.Label(new Rect(rect.x + 8, rect.y + 4, rect.width - 16, 18), label, labelStyle);
            GUI.Label(new Rect(rect.x + 8, rect.y + 23, rect.width - 16, 26), value, valueStyle);
        }

        private void DrawLine(Vector2 start, Vector2 end, float width, Color color)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            float angle = Vector2.SignedAngle(Vector2.right, end - start);
            GUIUtility.RotateAroundPivot(angle, start);
            DrawRect(
                new Rect(start.x, start.y - (width * 0.5f), Vector2.Distance(start, end), width),
                color);
            GUI.matrix = previousMatrix;
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
