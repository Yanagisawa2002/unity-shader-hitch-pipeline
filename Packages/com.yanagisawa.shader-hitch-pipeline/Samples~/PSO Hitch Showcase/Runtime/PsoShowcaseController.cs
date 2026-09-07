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
        private const int TotalStateTiles = 384;
        private const int StartupSeedTileCount = 8;
        private const int DeferredStateTileCount = TotalStateTiles - StartupSeedTileCount;
        private const int HistoryLength = 180;
        private const int RevealBatchSize = 2;
        private const int StartupTraceFrameCount = 12;
        private const float HitchThresholdMilliseconds = 8.33f;
        public const double ContentRequestDelaySeconds = 0.65;
        public const double DeferredDeadlineSeconds = 1.50;
        private const string VisualMarkerArgument = "-pso-showcase-marker";
        private const string DeferredPhase = "combat";

        private readonly List<MeshRenderer> deferredRenderers = new List<MeshRenderer>();
        private readonly List<Transform> motionTiles = new List<Transform>();
        private readonly List<Material> materials = new List<Material>();
        private readonly float[] frameHistory = new float[HistoryLength];
        private Texture2D white;
        private int historyCursor;
        private int historyCount;
        private int visibleCount;
        private int hitchFrameCount;
        private double readyAt;
        private double contentRequestAt;
        private double contentRevealAt;
        private double deferredReadyAt = -1.0;
        private double lastHitchAt = -1.0;
        private double presentationEpoch;
        private double measurementStartedAt;
        private double missedPresentationMilliseconds;
        private float currentFrameMilliseconds;
        private float worstFrameMilliseconds;
        private float lastHitchFrameMilliseconds;
        private bool sequenceStarted;
        private bool contentRequested;
        private bool contentRevealStarted;
        private bool deferredActivated;
        private bool deferredReady;
        private bool deadlineMissed;
        private int startupTraceFrames;
        private bool traceDeferredPhaseStarted;
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
            UpdateMotionScene();
            AdvanceTrainingTrace();

            if (contentRequested)
                RecordMeasuredFrame();

            if (!sequenceStarted)
            {
                if (!StartupWarmupReady())
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

            double now = Time.realtimeSinceStartupAsDouble;
            if (!contentRequested && now >= contentRequestAt)
                RequestDeferredContent(now);
            if (deferredActivated && !deferredReady)
                CheckDeferredReady(now);
            if (!contentRevealStarted && now >= contentRevealAt)
                BeginContentReveal(now);
            if (!contentRevealStarted ||
                deferredRenderers.Count == 0 ||
                visibleCount >= deferredRenderers.Count)
                return;

            // Pace first use at two states per presentation (target 240 Hz): the same
            // nominal 480 states/s reveal rate as eight states at 60 Hz, without creating
            // a showcase-only renderer activation batch. A cold hitch naturally lowers
            // presentation frequency while every mode still executes identical work.
            int batchCount = Math.Min(
                RevealBatchSize,
                deferredRenderers.Count - visibleCount);
            for (int count = 0; count < batchCount; count++)
            {
                deferredRenderers[visibleCount].enabled = true;
                visibleCount++;
            }
            if (visibleCount == deferredRenderers.Count)
                EmitVisualMarker("CONTENT_COMPLETE", now);
        }

        private void AdvanceTrainingTrace()
        {
            if (traceDeferredPhaseStarted)
                return;
            PsoTraceController trace = PsoTraceController.Instance;
            if (trace == null || !trace.IsTracing ||
                !string.Equals(trace.ActivePhase, "startup", StringComparison.OrdinalIgnoreCase))
                return;

            startupTraceFrames++;
            if (startupTraceFrames < StartupTraceFrameCount)
                return;

            traceDeferredPhaseStarted = true;
            trace.BeginPhase(DeferredPhase, "showcase", "deferred", "deadline");
            EmitVisualMarker("TRACE_DEFERRED_BEGIN", Time.realtimeSinceStartupAsDouble);
        }

        private void RequestDeferredContent(double now)
        {
            // The on-screen comparison is scoped to the deferred experiment. Capture and
            // player startup can legitimately jitter before this point, but those frames
            // are unrelated to either deferred policy. The benchmark receipt still keeps
            // the full WORKLOAD_START sequence; only the presentation HUD is re-armed here.
            ResetPresentationEvidence(now);
            contentRequested = true;
            EmitVisualMarker("CONTENT_REQUEST", now);

            if (IsBaselineMode())
                return;

            PsoWarmupOrchestrator orchestrator = PsoWarmupOrchestrator.Instance;
            if (orchestrator == null)
                throw new InvalidOperationException(
                    "Deferred showcase mode requires a warmup orchestrator.");
            deferredActivated = orchestrator.ActivatePhase(DeferredPhase);
            if (!deferredActivated)
                throw new InvalidOperationException(
                    "The installed plan has no deferred '" + DeferredPhase + "' phase.");
        }

        private void CheckDeferredReady(double now)
        {
            PsoWarmupOrchestrator orchestrator = PsoWarmupOrchestrator.Instance;
            if (orchestrator == null || orchestrator.HasFailed)
                return;
            if (!orchestrator.IsComplete)
                return;

            deferredReady = true;
            deferredReadyAt = now;
            EmitVisualMarker("DEFERRED_READY", now);
        }

        private void BeginContentReveal(double now)
        {
            contentRevealStarted = true;
            deadlineMissed = deferredActivated && !deferredReady;
            EmitVisualMarker("CONTENT_REVEAL", now);
        }

        private void BeginMeasuredWorkload()
        {
            sequenceStarted = true;
            measurementStartedAt = Time.realtimeSinceStartupAsDouble;
            contentRequestAt = measurementStartedAt + ContentRequestDelaySeconds;
            contentRevealAt = contentRequestAt + DeferredDeadlineSeconds;
            ResetPresentationEvidence(measurementStartedAt);
            EmitVisualMarker("WORKLOAD_START", measurementStartedAt);
        }

        private void ResetPresentationEvidence(double epoch)
        {
            presentationEpoch = epoch;
            Array.Clear(frameHistory, 0, frameHistory.Length);
            historyCursor = 0;
            historyCount = 0;
            currentFrameMilliseconds = 0.0f;
            worstFrameMilliseconds = 0.0f;
            missedPresentationMilliseconds = 0.0;
            hitchFrameCount = 0;
            lastHitchFrameMilliseconds = 0.0f;
            lastHitchAt = -1.0;
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
                lastHitchFrameMilliseconds = currentFrameMilliseconds;
                lastHitchAt = Time.realtimeSinceStartupAsDouble;
                missedPresentationMilliseconds +=
                    currentFrameMilliseconds - HitchThresholdMilliseconds;
            }
        }

        private bool StartupWarmupReady()
        {
            if (IsBaselineMode())
                return true;
            PsoWarmupOrchestrator orchestrator = PsoWarmupOrchestrator.Instance;
            return orchestrator == null || orchestrator.IsComplete;
        }

        private bool IsBaselineMode()
        {
            return PsoCommandLine.Current.HasFlag(PsoConstants.DisableWarmupArgument) ||
                   string.Equals(mode, "COLD / NO WARMUP", StringComparison.Ordinal);
        }

        private void UpdateMotionScene()
        {
            double elapsed = Time.realtimeSinceStartupAsDouble;
            for (int index = 0; index < motionTiles.Count; index++)
            {
                Transform tile = motionTiles[index];
                float x = Mathf.Repeat((float)(elapsed * 2.6) + index * 1.35f, 10.8f) - 5.4f;
                float y = 2.65f + Mathf.Sin((float)(elapsed * 2.2) + index * 0.73f) * 0.38f;
                tile.localPosition = new Vector3(x, y, index * 0.002f);
                tile.localRotation = Quaternion.Euler(
                    0.0f,
                    0.0f,
                    (float)(elapsed * 95.0) + index * 31.0f);
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(
                    Mathf.Sin((float)(elapsed * 0.9)) * 0.16f,
                    Mathf.Cos((float)(elapsed * 0.7)) * 0.08f,
                    -10.0f);
            }
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
            for (int index = 0; index < TotalStateTiles; index++)
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
                    Color.HSVToRGB(index / (float)TotalStateTiles, 0.72f, 0.95f));
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
                Collider collider = tile.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
                MeshRenderer renderer = tile.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                if (index < StartupSeedTileCount)
                {
                    tile.name = "Startup Motion Seed " + index;
                    tile.transform.localScale = Vector3.one * 0.34f;
                    renderer.enabled = true;
                    motionTiles.Add(tile.transform);
                }
                else
                {
                    int deferredIndex = index - StartupSeedTileCount;
                    int deferredRows = Mathf.CeilToInt(
                        DeferredStateTileCount / (float)Columns);
                    int column = deferredIndex % Columns;
                    int row = deferredIndex / Columns;
                    tile.name = "Deferred Combination " + deferredIndex;
                    tile.transform.localPosition = new Vector3(
                        (column - ((Columns - 1) * 0.5f)) * 0.32f,
                        (((deferredRows - 1) * 0.5f) - row) * 0.32f - 0.55f,
                        (deferredIndex % 7) * 0.002f);
                    tile.transform.localScale = Vector3.one * 0.27f;
                    renderer.enabled = false;
                    deferredRenderers.Add(renderer);
                }
            }
        }

        private void OnGUI()
        {
            if (white == null)
                return;

            GUIStyle title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
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
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = modeColor },
            };

            DrawPresentationMotion(small);
            GUI.Label(new Rect(24, 15, 780, 34), "SHADER HITCH PIPELINE · " + mode, title);
            GUI.Label(new Rect(25, 48, 760, 24),
                StartupSeedTileCount + " startup seed + " + DeferredStateTileCount +
                " deferred shader/graphics-state combinations · " +
                SystemInfo.graphicsDeviceType,
                small);

            Rect statusBanner = new Rect(24, 76, Screen.width - 48, 54);
            DrawRect(statusBanner, new Color(0.035f, 0.050f, 0.078f, 0.96f));
            DrawRect(new Rect(statusBanner.x, statusBanner.y, 8, statusBanner.height), modeColor);
            GUI.Label(statusBanner, StatusText(), status);

            DrawMetricCard(
                new Rect(24, 140, 190, 60),
                "CURRENT",
                currentFrameMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + " ms",
                small,
                currentFrameMilliseconds >= HitchThresholdMilliseconds
                    ? new Color(1.0f, 0.32f, 0.28f)
                    : new Color(0.35f, 0.86f, 1.0f));
            DrawMetricCard(
                new Rect(224, 140, 190, 60),
                "WORST",
                worstFrameMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + " ms",
                small,
                worstFrameMilliseconds >= HitchThresholdMilliseconds
                    ? new Color(1.0f, 0.32f, 0.28f)
                    : new Color(0.35f, 0.86f, 1.0f));
            DrawMetricCard(
                new Rect(424, 140, 270, 60),
                "MISSED PRESENTATION",
                missedPresentationMilliseconds.ToString("F1", CultureInfo.InvariantCulture) +
                " ms · " + hitchFrameCount + " frames",
                small,
                hitchFrameCount > 0
                    ? new Color(1.0f, 0.54f, 0.30f)
                    : new Color(0.32f, 0.92f, 0.68f));

            DrawMetricCard(
                new Rect(704, 140, 250, 60),
                "DEFERRED DEADLINE",
                DeadlineMetricText(),
                small,
                deadlineMissed
                    ? new Color(1.0f, 0.32f, 0.28f)
                    : deferredReady
                        ? new Color(0.32f, 0.92f, 0.68f)
                        : modeColor);

            if (lastHitchAt >= 0.0 &&
                Time.realtimeSinceStartupAsDouble - lastHitchAt <= 0.36)
            {
                Color alert = new Color(1.0f, 0.16f, 0.12f, 0.92f);
                DrawRect(new Rect(0, 0, Screen.width, 10), alert);
                DrawRect(new Rect(0, Screen.height - 10, Screen.width, 10), alert);
                DrawRect(new Rect(0, 0, 10, Screen.height), alert);
                DrawRect(new Rect(Screen.width - 10, 0, 10, Screen.height), alert);
                GUIStyle hitchStyle = new GUIStyle(status)
                {
                    fontSize = 28,
                    normal = { textColor = Color.white },
                };
                Rect hitchBanner = new Rect(
                    Screen.width * 0.5f - 230.0f,
                    212.0f,
                    460.0f,
                    52.0f);
                DrawRect(hitchBanner, new Color(0.72f, 0.04f, 0.03f, 0.94f));
                GUI.Label(
                    hitchBanner,
                    "ACTUAL " + lastHitchFrameMilliseconds.ToString(
                        "F1",
                        CultureInfo.InvariantCulture) + " ms FRAME",
                    hitchStyle);
            }

            Rect graph = new Rect(24, Screen.height - 170, Screen.width - 48, 136);
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
            string measurementLabel = contentRequested
                ? "Measurement reset at CONTENT_REQUEST · red = ≥ 8.33 ms / missed 120 FPS"
                : sequenceStarted
                ? "Measurement arms at CONTENT_REQUEST · red = ≥ 8.33 ms / missed 120 FPS"
                : "Measurement arms at WORKLOAD_START · presentation clock remains live";
            GUI.Label(
                new Rect(graph.x + 8, graph.y + 5, graph.width - 16, 22),
                measurementLabel + " · states: " +
                Mathf.Min(visibleCount, deferredRenderers.Count) + "/" +
                deferredRenderers.Count,
                small);
        }

        private string StatusText()
        {
            if (!sequenceStarted)
            {
                return readyAt < 0.0
                    ? "STARTUP HOT SET · PREPARING INTERACTIVE FRAME DOMAIN"
                    : "GAMEPLAY STARTS IN " + Math.Max(
                        0.0,
                        readyAt - Time.realtimeSinceStartupAsDouble).ToString(
                            "F1",
                            CultureInfo.InvariantCulture) + " s";
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!contentRequested)
            {
                return "GAMEPLAY LIVE · CONTENT REQUEST IN " + Math.Max(
                    0.0,
                    contentRequestAt - now).ToString("F1", CultureInfo.InvariantCulture) +
                       " s";
            }
            if (!contentRevealStarted)
            {
                double remaining = Math.Max(0.0, contentRevealAt - now);
                if (IsBaselineMode())
                    return "NO PREWARM · CONTENT NEEDED IN " +
                           remaining.ToString("F1", CultureInfo.InvariantCulture) + " s";
                if (deferredReady)
                    return "READY BEFORE DEADLINE · GAMEPLAY NEVER STOPPED";
                return string.Equals(mode, "UNITY / ALL-AT-ONCE", StringComparison.Ordinal)
                    ? "ALL-AT-ONCE WARMUP ACTIVE · WATCH THE MOTION"
                    : "DEADLINE SCHEDULER ACTIVE · " +
                      remaining.ToString("F1", CultureInfo.InvariantCulture) + " s LEFT";
            }
            if (visibleCount < deferredRenderers.Count)
                return "CONTENT REVEAL · " + visibleCount + " / " +
                       deferredRenderers.Count + " STATES";
            return "DEFERRED CONTENT COMPLETE · " + deferredRenderers.Count + " / " +
                   deferredRenderers.Count;
        }

        private string DeadlineMetricText()
        {
            if (!sequenceStarted || !contentRequested)
                return "ARMED · " + DeferredDeadlineSeconds.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) + " s";
            if (deadlineMissed)
                return "MISSED";
            if (deferredReadyAt >= 0.0)
            {
                return Math.Max(0.0, contentRevealAt - deferredReadyAt).ToString(
                    "F2",
                    CultureInfo.InvariantCulture) + " s EARLY";
            }
            if (IsBaselineMode())
                return "NO PLAN";
            return Math.Max(
                0.0,
                contentRevealAt - Time.realtimeSinceStartupAsDouble).ToString(
                    "F2",
                    CultureInfo.InvariantCulture) + " s LEFT";
        }

        private void DrawPresentationMotion(GUIStyle style)
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - presentationEpoch;
            float phase = (float)((elapsed * 0.34) % 1.0);
            float x = 24.0f + phase * (Screen.width - 48.0f);
            DrawRect(
                new Rect(x - 7.0f, 208.0f, 15.0f, Screen.height - 392.0f),
                new Color(0.15f, 0.85f, 1.0f, 0.08f));
            DrawRect(
                new Rect(x - 1.5f, 208.0f, 4.0f, Screen.height - 392.0f),
                new Color(0.38f, 0.94f, 1.0f, 0.78f));

            Vector2 center = new Vector2(Screen.width - 78.0f, 170.0f);
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
            PsoSystemMarkers.Emit("showcase-" + marker);
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
