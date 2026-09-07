using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yanagisawa.ShaderHitchPipeline.DeadlineRun
{
    [DefaultExecutionOrder(-1000)]
    public sealed class DeadlineRunController : MonoBehaviour
    {
        [Serializable]
        private struct ScenarioFrameSample
        {
            public double elapsedSeconds;
            public double milliseconds;
        }

        [Serializable]
        private sealed class ScenarioReceipt
        {
            public int schemaVersion = 1;
            public string scenario = "deadline-run";
            public string mode;
            public string phase;
            public string generatedUtc;
            public bool completed;
            public double runDurationSeconds;
            public double contentRequestSeconds;
            public double contentRevealSeconds;
            public double revealWindowEndSeconds;
            public double maximumMilliseconds;
            public double revealWindowMaximumMilliseconds;
            public double firstPostRevealFrameMilliseconds;
            public double severeFrameThresholdMilliseconds;
            public int severeFrameCount;
            public string graphicsDeviceType;
            public string graphicsDeviceName;
            public bool deferredReady;
            public bool deadlineMissed;
            public double deferredReadySeconds;
            public ScenarioFrameSample[] frameSamples;
        }

        public const double RunDurationSeconds = 12.0;
        public const double ContentRequestDelaySeconds = 2.0;
        public const double DeferredDeadlineSeconds = 2.5;

        private const double ContentCompleteDelaySeconds = 2.0;
        private const int StartupTraceFrameCount = 18;
        private const int FutureLayer = 8;
        private const int StartupStateCount = 12;
        private const int FutureStateCount = 320;
        private const float TargetFrameMilliseconds = 1000.0f / 240.0f;
        private const float SevereFrameMilliseconds = 16.67f;
        private const string DeferredPhase = "deadline-run-reveal";

        private static readonly string[] Keywords =
        {
            "DR_WET",
            "DR_EMISSION",
            "DR_GRID",
            "DR_DECAL",
            "DR_GLASS",
            "DR_SHIELD",
            "DR_RAIN",
            "DR_HOLOGRAM",
            "DR_DAMAGE",
            "DR_DISTORT",
        };

        private readonly List<Material> materials = new List<Material>();
        private readonly List<Transform> tunnelFans = new List<Transform>();
        private readonly List<Transform> rainStreaks = new List<Transform>();
        private readonly List<Transform> droneRoots = new List<Transform>();
        private readonly List<Transform> droneRotors = new List<Transform>();
        private readonly List<Transform> revealEffects = new List<Transform>();
        private readonly List<ScenarioFrameSample> scenarioFrameSamples =
            new List<ScenarioFrameSample>(4096);

        [SerializeField]
        private Shader deadlineRunShader;

        private PsoDeadlineScenario scenario;
        private PsoScenarioMarkerWriter markerWriter;
        private bool traceRequested;
        private Camera flightCamera;
        private GameObject futureRoot;
        private Transform trackedVehicle;
        private TrailRenderer trackedTrail;
        private Texture2D white;
        private double readyAt = -1.0;
        private double runCompletedAt = -1.0;
        private double lastSevereFrameAt = -1.0;
        private float currentFrameMilliseconds;
        private float worstFrameMilliseconds;
        private double firstPostRevealFrameMilliseconds = -1.0;
        private int severeFrameCount;
        private bool futureVisible;
        private bool runCompleteMarkerWritten;
        private bool scenarioReceiptWritten;
        private string scenarioReportPath;
        private string modeLabel;
        private Color modeColor;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle statusStyle;
        private GUIStyle proofStyle;
        private GUIStyle hitchStyle;
        private GUIStyle reticleStyle;
        private GUIStyle metricLabelStyle;
        private GUIStyle metricValueStyle;
        private string titleText;
        private string detailText;
        private string cachedStatusText;
        private string cachedPresentText;
        private string cachedWorstText;
        private string cachedMissedText;
        private string cachedClockText;
        private string cachedDeadlineText;
        private string cachedHitchText;
        private double nextHudTextRefreshAt;

        private void Awake()
        {
            traceRequested = PsoCommandLine.Current.HasFlag(
                PsoConstants.TraceArgument);
            Application.targetFrameRate = 240;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;

            markerWriter = new PsoScenarioMarkerWriter("DeadlineRun");
            scenarioReportPath = PsoCommandLine.Current.GetString(
                PsoConstants.ScenarioReportArgument,
                string.Empty);
            scenario = new PsoDeadlineScenario(
                DeferredPhase,
                "deadline-run",
                ContentRequestDelaySeconds,
                DeferredDeadlineSeconds,
                StartupTraceFrameCount,
                markerWriter.Write);
            ConfigureMode();
            titleText = "DEADLINE RUN · " + modeLabel;
            detailText = "12 s fixed flight · " + FutureStateCount +
                         " real upcoming shader/render-state combinations · " +
                         SystemInfo.graphicsDeviceType;
            SetupCamera();
            BuildWorld();
            UpdateFlight(0.0);
            white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();

            double delaySeconds = PsoCommandLine.Current.GetDouble(
                PsoConstants.BenchmarkDelayArgument,
                1.0,
                0.0,
                3600.0);
            if (scenario.Mode == PsoDeadlineScenarioMode.Baseline)
                readyAt = Time.realtimeSinceStartupAsDouble + delaySeconds;

            markerWriter.Write("CAPTURE_READY", Time.realtimeSinceStartupAsDouble);
        }

        private void ConfigureMode()
        {
            switch (scenario.Mode)
            {
                case PsoDeadlineScenarioMode.Baseline:
                    modeLabel = "UNITY DEFAULT / COLD";
                    modeColor = new Color(1.0f, 0.28f, 0.22f);
                    break;
                case PsoDeadlineScenarioMode.Throughput:
                    modeLabel = "UNITY WARM ALL NOW";
                    modeColor = new Color(1.0f, 0.68f, 0.18f);
                    break;
                default:
                    modeLabel = "OURS / DEADLINE SCHEDULED";
                    modeColor = new Color(0.18f, 0.94f, 0.68f);
                    break;
            }
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (runCompleteMarkerWritten)
            {
                TryQuitAfterMeasurement();
                return;
            }
            if (traceRequested)
                scenario.AdvanceTrainingTrace(now);

            if (!scenario.IsArmed)
            {
                UpdateIdleMotion(now);
                if (!scenario.IsStartupReady)
                    return;
                if (readyAt < 0.0)
                {
                    double delaySeconds = PsoCommandLine.Current.GetDouble(
                        PsoConstants.BenchmarkDelayArgument,
                        1.0,
                        0.0,
                        3600.0);
                    readyAt = now + delaySeconds;
                }
                if (now < readyAt)
                    return;

                BeginRun(now);
            }

            RecordFrame(now);
            scenario.Tick(now);
            if (scenario.IsContentRevealed && !futureVisible)
                RevealFutureWorld();

            double elapsed = Math.Max(0.0, now - scenario.WorkloadStartedAt);
            UpdateFlight(elapsed);

            if (scenario.IsContentRevealed &&
                !scenario.IsContentCompleted &&
                now >= scenario.ContentRevealAt + ContentCompleteDelaySeconds)
            {
                scenario.MarkContentComplete(now);
            }

            if (!runCompleteMarkerWritten && elapsed >= RunDurationSeconds)
            {
                runCompleteMarkerWritten = true;
                runCompletedAt = now;
                scenario.CompleteMeasurement(now);
                markerWriter.Write("RUN_COMPLETE", now);
            }
        }

        private void TryQuitAfterMeasurement()
        {
            if (!PsoCommandLine.Current.HasFlag(
                    PsoConstants.ScenarioQuitOnCompleteArgument) ||
                !PsoBenchmarkMeasurementGate.IsSamplerFinalized)
            {
                return;
            }
            WriteScenarioReceipt(true);
            markerWriter.Flush();
            Application.Quit();
        }

        private void BeginRun(double now)
        {
            currentFrameMilliseconds = 0.0f;
            worstFrameMilliseconds = 0.0f;
            lastSevereFrameAt = -1.0;
            severeFrameCount = 0;
            scenario.Arm(now);
            if (trackedTrail != null)
            {
                trackedTrail.Clear();
                trackedTrail.emitting = false;
            }
            UpdateFlight(0.0);
        }

        private void RecordFrame(double now)
        {
            currentFrameMilliseconds = Time.unscaledDeltaTime * 1000.0f;
            double elapsed = Math.Max(0.0, now - scenario.WorkloadStartedAt);
            if (elapsed > RunDurationSeconds + 0.25)
                return;

            scenarioFrameSamples.Add(new ScenarioFrameSample
            {
                elapsedSeconds = elapsed,
                milliseconds = currentFrameMilliseconds,
            });
            if (futureVisible && firstPostRevealFrameMilliseconds < 0.0)
                firstPostRevealFrameMilliseconds = currentFrameMilliseconds;
            worstFrameMilliseconds = Mathf.Max(worstFrameMilliseconds, currentFrameMilliseconds);
            if (currentFrameMilliseconds < SevereFrameMilliseconds)
                return;

            severeFrameCount++;
            lastSevereFrameAt = now;
            cachedHitchText = "ACTUAL " + currentFrameMilliseconds.ToString(
                "F1",
                CultureInfo.InvariantCulture) + " ms FRAME";
        }

        private void RevealFutureWorld()
        {
            futureVisible = true;
            flightCamera.cullingMask |= 1 << FutureLayer;
            if (trackedTrail != null)
            {
                trackedTrail.Clear();
                trackedTrail.emitting = true;
            }
        }

        private void UpdateIdleMotion(double now)
        {
            for (int index = 0; index < tunnelFans.Count; index++)
            {
                tunnelFans[index].localRotation = Quaternion.Euler(
                    0.0f,
                    0.0f,
                    (float)(now * 210.0 + index * 37.0));
            }
        }

        private void UpdateFlight(double elapsed)
        {
            float time = (float)Math.Min(RunDurationSeconds, elapsed);
            float revealTime = (float)(ContentRequestDelaySeconds + DeferredDeadlineSeconds);
            float revealBlend = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(
                revealTime - 0.6f,
                revealTime + 1.2f,
                time));

            float z = -26.0f + time * 6.5f;
            float x = Mathf.Sin(time * 0.72f) * Mathf.Lerp(0.35f, 1.8f, revealBlend);
            float y = 2.15f + Mathf.Sin(time * 1.55f) * 0.10f + revealBlend * 0.32f;
            Vector3 cameraPosition = new Vector3(x, y, z);
            Vector3 lookPoint = cameraPosition + new Vector3(
                Mathf.Sin(time * 0.47f) * 0.9f,
                0.08f + revealBlend * 0.20f,
                14.0f);
            Quaternion lookRotation = Quaternion.LookRotation(
                lookPoint - cameraPosition,
                Vector3.up);
            float bank = Mathf.Sin(time * 0.83f) * Mathf.Lerp(0.8f, 2.8f, revealBlend);
            flightCamera.transform.SetPositionAndRotation(
                cameraPosition,
                lookRotation * Quaternion.AngleAxis(bank, Vector3.forward));

            for (int index = 0; index < tunnelFans.Count; index++)
            {
                tunnelFans[index].localRotation = Quaternion.Euler(
                    0.0f,
                    0.0f,
                    time * 210.0f + index * 37.0f);
            }

            float afterReveal = Mathf.Max(0.0f, time - revealTime);
            if (trackedVehicle != null)
            {
                trackedVehicle.position = new Vector3(
                    Mathf.Sin(afterReveal * 1.45f) * 2.8f,
                    0.58f + Mathf.Sin(afterReveal * 3.0f) * 0.08f,
                    16.0f + afterReveal * 6.5f);
                trackedVehicle.rotation = Quaternion.Euler(
                    0.0f,
                    Mathf.Sin(afterReveal * 1.45f) * 12.0f,
                    0.0f);
            }

            for (int index = 0; index < rainStreaks.Count; index++)
            {
                float rainX = Mathf.Repeat(index * 1.731f + time * 2.8f, 20.0f) - 10.0f;
                float rainY = Mathf.Repeat(index * 0.917f - time * 15.0f, 9.5f) - 0.8f;
                float rainZ = z + 7.0f + (index % 12) * 2.15f;
                rainStreaks[index].position = new Vector3(rainX, rainY, rainZ);
            }

            Vector3 vehiclePosition = trackedVehicle == null
                ? new Vector3(0.0f, 1.0f, z + 13.0f)
                : trackedVehicle.position;
            for (int index = 0; index < droneRoots.Count; index++)
            {
                float angle = time * (0.8f + index * 0.04f) + index * 0.78f;
                float radius = 3.8f + (index % 3) * 1.1f;
                droneRoots[index].position = vehiclePosition + new Vector3(
                    Mathf.Sin(angle) * radius,
                    2.0f + Mathf.Sin(angle * 1.7f) * 0.7f,
                    4.0f + Mathf.Cos(angle) * radius);
                droneRoots[index].rotation = Quaternion.Euler(
                    Mathf.Sin(angle) * 8.0f,
                    angle * Mathf.Rad2Deg + 180.0f,
                    Mathf.Cos(angle) * 12.0f);
            }
            for (int index = 0; index < droneRotors.Count; index++)
            {
                droneRotors[index].localRotation = Quaternion.Euler(
                    0.0f,
                    time * 980.0f + index * 29.0f,
                    0.0f);
            }

            for (int index = 0; index < revealEffects.Count; index++)
            {
                Transform effect = revealEffects[index];
                float pulse = 0.78f + 0.30f * Mathf.Sin(time * 4.0f + index * 0.63f);
                effect.localScale = Vector3.one * pulse * (0.55f + (index % 5) * 0.11f);
                effect.localRotation = Quaternion.Euler(
                    time * 23.0f + index * 7.0f,
                    time * 37.0f + index * 11.0f,
                    0.0f);
            }
        }

        private void SetupCamera()
        {
            GameObject cameraObject = new GameObject("Deadline Run Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(transform, false);
            flightCamera = cameraObject.AddComponent<Camera>();
            flightCamera.clearFlags = CameraClearFlags.SolidColor;
            flightCamera.backgroundColor = new Color(0.006f, 0.012f, 0.028f, 1.0f);
            flightCamera.fieldOfView = 69.0f;
            flightCamera.nearClipPlane = 0.08f;
            flightCamera.farClipPlane = 180.0f;
            flightCamera.allowHDR = true;
            flightCamera.allowMSAA = false;
            flightCamera.cullingMask &= ~(1 << FutureLayer);
        }

        private void BuildWorld()
        {
            Shader shader = deadlineRunShader != null
                ? deadlineRunShader
                : Shader.Find("Yanagisawa/Deadline Run");
            if (shader == null)
                throw new InvalidOperationException("Deadline Run shader was not included in the build.");

            var startupRoot = new GameObject("Approach Tunnel");
            startupRoot.transform.SetParent(transform, false);
            futureRoot = new GameObject("Predicted Combat Airspace");
            futureRoot.transform.SetParent(transform, false);
            SetLayer(futureRoot, FutureLayer);

            Material[] startupMaterials = new Material[StartupStateCount];
            for (int index = 0; index < startupMaterials.Length; index++)
            {
                startupMaterials[index] = CreateStateMaterial(
                    shader,
                    index,
                    Color.Lerp(
                        new Color(0.04f, 0.16f, 0.24f),
                        new Color(0.12f, 0.62f, 0.88f),
                        index / (float)(StartupStateCount - 1)),
                    false);
            }

            Material[] futureMaterials = new Material[FutureStateCount];
            for (int index = 0; index < futureMaterials.Length; index++)
            {
                Color baseColor = Color.HSVToRGB(
                    Mathf.Repeat(0.52f + index * 0.0137f, 1.0f),
                    0.55f + (index % 4) * 0.09f,
                    0.72f + (index % 3) * 0.12f);
                futureMaterials[index] = CreateStateMaterial(
                    shader,
                    32 + index,
                    baseColor,
                    true);
            }

            BuildTunnel(startupRoot.transform, startupMaterials);
            BuildFutureCity(futureRoot.transform, futureMaterials);
        }

        private Material CreateStateMaterial(
            Shader shader,
            int stateIndex,
            Color baseColor,
            bool future)
        {
            var material = new Material(shader)
            {
                name = (future ? "Future PSO " : "Startup PSO ") + stateIndex,
            };
            for (int bit = 0; bit < Keywords.Length; bit++)
            {
                if ((stateIndex & (1 << bit)) != 0)
                    material.EnableKeyword(Keywords[bit]);
            }

            bool transparent = stateIndex % 5 == 0 || stateIndex % 11 == 0;
            bool additive = stateIndex % 7 == 0;
            material.renderQueue = transparent || additive ? 3000 : 2000;
            material.SetColor("_BaseColor", new Color(
                baseColor.r,
                baseColor.g,
                baseColor.b,
                transparent ? 0.36f : additive ? 0.62f : 1.0f));
            material.SetColor(
                "_EmissionColor",
                Color.Lerp(baseColor, Color.white, 0.28f) * (future ? 1.35f : 0.72f));
            material.SetFloat("_Pulse", 0.45f + (stateIndex % 9) * 0.08f);
            material.SetInt("_SrcBlend", (int)(additive
                ? BlendMode.One
                : transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetInt("_DstBlend", (int)(additive
                ? BlendMode.One
                : transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetInt("_ZWrite", transparent || additive ? 0 : 1);
            material.SetInt("_Cull", (int)(stateIndex % 3 == 0
                ? CullMode.Off
                : CullMode.Back));
            material.SetInt("_ZTest", (int)(stateIndex % 13 == 0
                ? CompareFunction.Always
                : CompareFunction.LessEqual));
            materials.Add(material);
            return material;
        }

        private void BuildTunnel(Transform parent, Material[] startupMaterials)
        {
            CreatePrimitive(
                PrimitiveType.Cube,
                "Tunnel Floor",
                parent,
                new Vector3(0.0f, -0.45f, -9.0f),
                new Vector3(12.0f, 0.5f, 50.0f),
                Quaternion.identity,
                startupMaterials[0],
                0);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Tunnel Ceiling",
                parent,
                new Vector3(0.0f, 6.1f, -9.0f),
                new Vector3(12.0f, 0.35f, 50.0f),
                Quaternion.identity,
                startupMaterials[1],
                0);
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePrimitive(
                    PrimitiveType.Cube,
                    side < 0 ? "Tunnel Left Wall" : "Tunnel Right Wall",
                    parent,
                    new Vector3(side * 6.0f, 2.8f, -9.0f),
                    new Vector3(0.35f, 6.5f, 50.0f),
                    Quaternion.identity,
                    startupMaterials[2 + (side > 0 ? 1 : 0)],
                    0);
            }

            for (int index = 0; index < 12; index++)
            {
                float z = -29.0f + index * 3.4f;
                Material frameMaterial = startupMaterials[4 + index % 4];
                CreatePrimitive(
                    PrimitiveType.Cube,
                    "Tunnel Arch Top " + index,
                    parent,
                    new Vector3(0.0f, 5.5f, z),
                    new Vector3(11.6f, 0.18f, 0.16f),
                    Quaternion.identity,
                    frameMaterial,
                    0);
                for (int side = -1; side <= 1; side += 2)
                {
                    CreatePrimitive(
                        PrimitiveType.Cube,
                        "Tunnel Arch Side " + index + " " + side,
                        parent,
                        new Vector3(side * 5.45f, 2.55f, z),
                        new Vector3(0.18f, 5.9f, 0.16f),
                        Quaternion.identity,
                        frameMaterial,
                        0);
                    CreatePrimitive(
                        PrimitiveType.Cube,
                        "Tunnel Light " + index + " " + side,
                        parent,
                        new Vector3(side * 4.6f, 1.15f, z + 0.08f),
                        new Vector3(0.10f, 0.12f, 1.9f),
                        Quaternion.identity,
                        startupMaterials[8 + index % 4],
                        0);
                }
            }

            for (int index = 0; index < 4; index++)
            {
                var fan = new GameObject("Continuity Turbine " + index);
                fan.transform.SetParent(parent, false);
                fan.transform.localPosition = new Vector3(
                    index % 2 == 0 ? -4.65f : 4.65f,
                    4.15f,
                    -21.0f + index * 7.2f);
                CreatePrimitive(
                    PrimitiveType.Cylinder,
                    "Hub",
                    fan.transform,
                    Vector3.zero,
                    new Vector3(0.24f, 0.18f, 0.24f),
                    Quaternion.Euler(90.0f, 0.0f, 0.0f),
                    startupMaterials[9],
                    0);
                for (int blade = 0; blade < 4; blade++)
                {
                    CreatePrimitive(
                        PrimitiveType.Cube,
                        "Blade " + blade,
                        fan.transform,
                        Quaternion.Euler(0.0f, 0.0f, blade * 90.0f) *
                            new Vector3(0.0f, 0.78f, 0.0f),
                        new Vector3(0.16f, 1.3f, 0.08f),
                        Quaternion.Euler(0.0f, 0.0f, blade * 90.0f),
                        startupMaterials[10 + blade % 2],
                        0);
                }
                tunnelFans.Add(fan.transform);
            }
        }

        private void BuildFutureCity(Transform parent, Material[] futureMaterials)
        {
            int materialIndex = 0;
            CreatePrimitive(
                PrimitiveType.Cube,
                "Wet Expressway",
                parent,
                new Vector3(0.0f, -0.30f, 47.0f),
                new Vector3(15.0f, 0.30f, 88.0f),
                Quaternion.identity,
                futureMaterials[0],
                FutureLayer);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Storm Horizon",
                parent,
                new Vector3(0.0f, 15.0f, 92.0f),
                new Vector3(60.0f, 30.0f, 0.35f),
                Quaternion.identity,
                futureMaterials[1],
                FutureLayer);

            for (int building = 0; building < 20; building++)
            {
                int side = building % 2 == 0 ? -1 : 1;
                int lane = building / 2;
                float z = 12.0f + lane * 7.0f;
                float height = 8.0f + (building % 5) * 2.15f;
                float x = side * (9.0f + (lane % 3) * 1.6f);
                Material bodyMaterial = futureMaterials[materialIndex++];
                CreatePrimitive(
                    PrimitiveType.Cube,
                    "City Tower " + building,
                    parent,
                    new Vector3(x, height * 0.5f - 0.1f, z),
                    new Vector3(5.2f, height, 5.0f),
                    Quaternion.identity,
                    bodyMaterial,
                    FutureLayer);
                for (int window = 0; window < 4; window++)
                {
                    float windowY = 1.4f + window * Mathf.Max(1.35f, height / 5.0f);
                    CreatePrimitive(
                        PrimitiveType.Cube,
                        "Tower Window " + building + " " + window,
                        parent,
                        new Vector3(
                            side * (Mathf.Abs(x) - 2.64f),
                            windowY,
                            z + (window % 2 == 0 ? -1.25f : 1.25f)),
                        new Vector3(0.08f, 0.42f, 1.65f),
                        Quaternion.identity,
                        futureMaterials[materialIndex++],
                        FutureLayer);
                }
            }

            for (int light = 0; light < 40; light++)
            {
                int side = light % 2 == 0 ? -1 : 1;
                CreatePrimitive(
                    PrimitiveType.Cube,
                    "Road Guidance Light " + light,
                    parent,
                    new Vector3(side * 4.65f, -0.02f, 7.0f + light * 1.8f),
                    new Vector3(0.16f, 0.08f, 0.72f),
                    Quaternion.identity,
                    futureMaterials[materialIndex++],
                    FutureLayer);
            }

            for (int rain = 0; rain < 96; rain++)
            {
                GameObject streak = CreatePrimitive(
                    PrimitiveType.Cube,
                    "Actual Rain Streak " + rain,
                    parent,
                    new Vector3(0.0f, -20.0f, 0.0f),
                    new Vector3(0.018f + (rain % 3) * 0.008f, 0.72f, 0.018f),
                    Quaternion.Euler(0.0f, 0.0f, -12.0f),
                    futureMaterials[materialIndex++],
                    FutureLayer);
                rainStreaks.Add(streak.transform);
            }

            for (int drone = 0; drone < 8; drone++)
            {
                var droneRoot = new GameObject("Enemy Drone " + drone);
                droneRoot.layer = FutureLayer;
                droneRoot.transform.SetParent(parent, false);
                droneRoots.Add(droneRoot.transform);
                CreatePrimitive(
                    PrimitiveType.Sphere,
                    "Drone Core",
                    droneRoot.transform,
                    Vector3.zero,
                    new Vector3(0.85f, 0.32f, 1.1f),
                    Quaternion.identity,
                    futureMaterials[materialIndex++],
                    FutureLayer);
                CreatePrimitive(
                    PrimitiveType.Cube,
                    "Drone Spine",
                    droneRoot.transform,
                    new Vector3(0.0f, 0.0f, -0.1f),
                    new Vector3(2.4f, 0.10f, 0.18f),
                    Quaternion.identity,
                    futureMaterials[materialIndex++],
                    FutureLayer);
                for (int rotor = 0; rotor < 4; rotor++)
                {
                    float rotorX = rotor < 2 ? -1.0f : 1.0f;
                    float rotorZ = rotor % 2 == 0 ? -0.48f : 0.48f;
                    GameObject rotorObject = CreatePrimitive(
                        PrimitiveType.Cylinder,
                        "Drone Rotor " + rotor,
                        droneRoot.transform,
                        new Vector3(rotorX, 0.05f, rotorZ),
                        new Vector3(0.42f, 0.025f, 0.42f),
                        Quaternion.identity,
                        futureMaterials[materialIndex++],
                        FutureLayer);
                    droneRotors.Add(rotorObject.transform);
                }
            }

            for (int effect = 0; effect < 36; effect++)
            {
                float angle = effect * Mathf.PI * 2.0f / 36.0f;
                float radius = 5.0f + (effect % 6) * 1.5f;
                GameObject effectObject = CreatePrimitive(
                    effect % 3 == 0 ? PrimitiveType.Sphere : PrimitiveType.Quad,
                    effect % 3 == 0
                        ? "Shield Impact " + effect
                        : "Combat Burst " + effect,
                    parent,
                    new Vector3(
                        Mathf.Sin(angle) * radius,
                        1.2f + (effect % 7) * 0.8f,
                        19.0f + Mathf.Cos(angle) * radius + (effect / 9) * 8.0f),
                    Vector3.one,
                    effect % 3 == 0
                        ? Quaternion.identity
                        : Quaternion.Euler(0.0f, 180.0f, angle * Mathf.Rad2Deg),
                    futureMaterials[materialIndex++],
                    FutureLayer);
                revealEffects.Add(effectObject.transform);
            }

            if (materialIndex != FutureStateCount)
            {
                throw new InvalidOperationException(
                    "Deadline Run assigned " + materialIndex + " future states; expected " +
                    FutureStateCount + ".");
            }

            GameObject vehicle = CreatePrimitive(
                PrimitiveType.Cube,
                "Tracked Evac Vehicle",
                parent,
                new Vector3(0.0f, 0.58f, 16.0f),
                new Vector3(1.6f, 0.45f, 3.0f),
                Quaternion.identity,
                futureMaterials[9],
                FutureLayer);
            trackedVehicle = vehicle.transform;
            trackedTrail = vehicle.AddComponent<TrailRenderer>();
            trackedTrail.time = 1.25f;
            trackedTrail.minVertexDistance = 0.04f;
            trackedTrail.widthMultiplier = 0.16f;
            trackedTrail.sharedMaterial = futureMaterials[10];
            trackedTrail.startColor = new Color(0.15f, 0.95f, 1.0f, 0.95f);
            trackedTrail.endColor = new Color(0.15f, 0.45f, 1.0f, 0.0f);
            trackedTrail.emitting = false;

            for (int index = 0; index < 28; index++)
            {
                int side = index % 2 == 0 ? -1 : 1;
                CreatePrimitive(
                    PrimitiveType.Cube,
                    "Expressway Rail " + index,
                    parent,
                    new Vector3(side * 6.6f, 0.48f, 7.0f + index * 2.8f),
                    new Vector3(0.14f, 0.58f, 2.4f),
                    Quaternion.identity,
                    futureMaterials[14 + index % 8],
                    FutureLayer);
            }
        }

        private static GameObject CreatePrimitive(
            PrimitiveType type,
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material,
            int layer)
        {
            GameObject result = GameObject.CreatePrimitive(type);
            result.name = objectName;
            result.layer = layer;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = localPosition;
            result.transform.localScale = localScale;
            result.transform.localRotation = localRotation;
            Collider collider = result.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);
            Renderer renderer = result.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            return result;
        }

        private static void SetLayer(GameObject root, int layer)
        {
            root.layer = layer;
            for (int index = 0; index < root.transform.childCount; index++)
                SetLayer(root.transform.GetChild(index).gameObject, layer);
        }

        private void OnGUI()
        {
            if (white == null)
                return;

            EnsureGuiStyles();
            RefreshHudText(Time.realtimeSinceStartupAsDouble);

            DrawRect(new Rect(0, 0, Screen.width, 74), new Color(0.012f, 0.025f, 0.052f, 0.88f));
            GUI.Label(new Rect(24, 12, 580, 32), titleText, titleStyle);
            GUI.Label(
                new Rect(25, 44, 760, 24),
                detailText,
                labelStyle);
            GUI.Label(
                new Rect(Screen.width - 470, 18, 445, 44),
                "SAME CAMERA · SAME CONTENT\nNO SLEEP · NO STREAMING · PREALLOCATED BEFORE MEASUREMENT",
                proofStyle);

            Rect statusRect = new Rect(24, 88, Screen.width - 48, 48);
            DrawRect(statusRect, new Color(0.015f, 0.038f, 0.070f, 0.88f));
            DrawRect(new Rect(statusRect.x, statusRect.y, 7, statusRect.height), modeColor);
            GUI.Label(statusRect, cachedStatusText, statusStyle);

            if (futureVisible && trackedVehicle != null)
                DrawTargetReticle();

            Rect currentRect = new Rect(24, Screen.height - 88, 190, 56);
            Rect worstRect = new Rect(224, Screen.height - 88, 190, 56);
            Rect missedRect = new Rect(424, Screen.height - 88, 250, 56);
            Rect clockRect = new Rect(684, Screen.height - 88, 292, 56);
            Rect deadlineRect = new Rect(Screen.width - 294, Screen.height - 88, 270, 56);
            DrawMetric(
                currentRect,
                "PRESENT",
                cachedPresentText,
                currentFrameMilliseconds);
            DrawMetric(
                worstRect,
                "WORST",
                cachedWorstText,
                worstFrameMilliseconds);
            DrawTextMetric(
                missedRect,
                "MISSED 60 FPS",
                cachedMissedText,
                severeFrameCount == 0 ? modeColor : new Color(1.0f, 0.28f, 0.22f));
            DrawTextMetric(
                clockRect,
                "FLIGHT CLOCK",
                cachedClockText,
                modeColor);
            DrawTextMetric(
                deadlineRect,
                "CONTENT DEADLINE",
                cachedDeadlineText,
                scenario.DeadlineMissed ? new Color(1.0f, 0.28f, 0.22f) : modeColor);

            if (lastSevereFrameAt >= 0.0 &&
                Time.realtimeSinceStartupAsDouble - lastSevereFrameAt <= 0.80)
            {
                DrawRect(new Rect(0, 0, Screen.width, 8), new Color(1.0f, 0.08f, 0.04f, 0.96f));
                DrawRect(new Rect(0, Screen.height - 8, Screen.width, 8), new Color(1.0f, 0.08f, 0.04f, 0.96f));
                Rect alert = new Rect(Screen.width * 0.5f - 250.0f, 154.0f, 500.0f, 58.0f);
                DrawRect(alert, new Color(0.70f, 0.025f, 0.018f, 0.94f));
                GUI.Label(alert, cachedHitchText, hitchStyle);
            }
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.76f, 0.88f, 0.96f) },
            };
            statusStyle = new GUIStyle(labelStyle)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = modeColor },
            };
            proofStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.UpperRight,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.48f, 0.82f, 1.0f) },
            };
            hitchStyle = new GUIStyle(statusStyle)
            {
                fontSize = 30,
                normal = { textColor = Color.white },
            };
            reticleStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.18f, 0.95f, 1.0f, 0.96f) },
            };
            metricLabelStyle = new GUIStyle(labelStyle)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.50f, 0.70f, 0.84f) },
            };
            metricValueStyle = new GUIStyle(labelStyle)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
            };
        }

        private void RefreshHudText(double now)
        {
            if (now < nextHudTextRefreshAt && !string.IsNullOrEmpty(cachedStatusText))
                return;

            nextHudTextRefreshAt = now + 1.0 / 30.0;
            cachedStatusText = StatusText();
            cachedPresentText = currentFrameMilliseconds.ToString(
                "F2",
                CultureInfo.InvariantCulture) + " ms";
            cachedWorstText = worstFrameMilliseconds.ToString(
                "F2",
                CultureInfo.InvariantCulture) + " ms";
            cachedMissedText = severeFrameCount + " frames";
            double flightElapsed = scenario.IsArmed
                ? Math.Min(
                    RunDurationSeconds,
                    Math.Max(0.0, now - scenario.WorkloadStartedAt))
                : 0.0;
            cachedClockText = "T+" + flightElapsed.ToString(
                                  "F2",
                                  CultureInfo.InvariantCulture) +
                              " / 12.00 s";
            cachedDeadlineText = DeadlineText();
        }

        private string StatusText()
        {
            if (!scenario.IsArmed)
            {
                return readyAt < 0.0
                    ? "STARTUP HOT SET · HOLDING FIRST PRESENT"
                    : "FLIGHT STARTS IN " + Math.Max(
                        0.0,
                        readyAt - Time.realtimeSinceStartupAsDouble).ToString(
                            "F1",
                            CultureInfo.InvariantCulture) + " s";
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!scenario.IsContentRequested)
            {
                return "HIGH-SPEED APPROACH · PREDICTION GATE IN " +
                       scenario.SecondsUntilRequest(now).ToString(
                           "F1",
                           CultureInfo.InvariantCulture) + " s";
            }
            if (!scenario.IsContentRevealed)
            {
                string remaining = scenario.SecondsUntilReveal(now).ToString(
                    "F1",
                    CultureInfo.InvariantCulture);
                if (scenario.Mode == PsoDeadlineScenarioMode.Baseline)
                    return "NO PREWARM · COMBAT AIRSPACE REQUIRED IN " + remaining + " s";
                if (scenario.IsDeferredReady)
                    return "UPCOMING AIRSPACE READY · FLIGHT NEVER STOPPED";
                return scenario.Mode == PsoDeadlineScenarioMode.Throughput
                    ? "ALL-AT-ONCE WARMUP · WATCH THE TUNNEL MOTION"
                    : "DEADLINE + COST + HOT-SET SCHEDULER · " + remaining + " s LEFT";
            }
            if (!scenario.IsContentCompleted)
                return "COMBAT AIRSPACE ONLINE · TARGET LOCK MAINTAINED";
            return runCompletedAt < 0.0
                ? "REVEAL COMPLETE · CONTINUOUS PURSUIT"
                : "12 SECOND RUN COMPLETE · RAW RECEIPTS RETAINED";
        }

        private string DeadlineText()
        {
            if (!scenario.IsContentRequested)
                return DeferredDeadlineSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s ARMED";
            if (scenario.DeadlineMissed)
                return "MISSED";
            if (scenario.IsDeferredReady)
            {
                return Math.Max(0.0, scenario.ContentRevealAt - scenario.DeferredReadyAt)
                    .ToString("F2", CultureInfo.InvariantCulture) + " s EARLY";
            }
            if (scenario.Mode == PsoDeadlineScenarioMode.Baseline)
                return "NO PLAN";
            return scenario.SecondsUntilReveal(Time.realtimeSinceStartupAsDouble)
                .ToString("F2", CultureInfo.InvariantCulture) + " s LEFT";
        }

        private void DrawTargetReticle()
        {
            Vector3 screen = flightCamera.WorldToScreenPoint(trackedVehicle.position);
            if (screen.z <= 0.0f)
                return;
            float x = screen.x;
            float y = Screen.height - screen.y;
            Color color = new Color(0.18f, 0.95f, 1.0f, 0.96f);
            float size = 54.0f;
            float arm = 16.0f;
            DrawRect(new Rect(x - size, y - size, arm, 2), color);
            DrawRect(new Rect(x - size, y - size, 2, arm), color);
            DrawRect(new Rect(x + size - arm, y - size, arm, 2), color);
            DrawRect(new Rect(x + size - 2, y - size, 2, arm), color);
            DrawRect(new Rect(x - size, y + size - 2, arm, 2), color);
            DrawRect(new Rect(x - size, y + size - arm, 2, arm), color);
            DrawRect(new Rect(x + size - arm, y + size - 2, arm, 2), color);
            DrawRect(new Rect(x + size - 2, y + size - arm, 2, arm), color);
            GUI.Label(
                new Rect(x - 90, y + size + 7, 180, 24),
                "TARGET LOCK · LIVE",
                reticleStyle);
        }

        private void DrawMetric(
            Rect rect,
            string labelText,
            string valueText,
            float value)
        {
            DrawTextMetric(
                rect,
                labelText,
                valueText,
                value >= SevereFrameMilliseconds
                    ? new Color(1.0f, 0.28f, 0.22f)
                    : value >= TargetFrameMilliseconds
                        ? new Color(1.0f, 0.72f, 0.24f)
                        : modeColor);
        }

        private void DrawTextMetric(
            Rect rect,
            string labelText,
            string value,
            Color valueColor)
        {
            DrawRect(rect, new Color(0.012f, 0.030f, 0.058f, 0.90f));
            metricValueStyle.normal.textColor = valueColor;
            GUI.Label(
                new Rect(rect.x + 9, rect.y + 4, rect.width - 18, 18),
                labelText,
                metricLabelStyle);
            GUI.Label(
                new Rect(rect.x + 9, rect.y + 23, rect.width - 18, 27),
                value,
                metricValueStyle);
        }

        private void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        private void WriteScenarioReceipt(bool completed)
        {
            if (scenarioReceiptWritten || string.IsNullOrWhiteSpace(scenarioReportPath))
                return;
            scenarioReceiptWritten = true;

            double maximum = 0.0;
            double revealMaximum = 0.0;
            double revealStart = ContentRequestDelaySeconds + DeferredDeadlineSeconds;
            double revealEnd = revealStart + ContentCompleteDelaySeconds;
            for (int index = 0; index < scenarioFrameSamples.Count; index++)
            {
                ScenarioFrameSample sample = scenarioFrameSamples[index];
                maximum = Math.Max(maximum, sample.milliseconds);
                if (sample.elapsedSeconds >= revealStart && sample.elapsedSeconds <= revealEnd)
                    revealMaximum = Math.Max(revealMaximum, sample.milliseconds);
            }
            if (firstPostRevealFrameMilliseconds > 0.0)
            {
                revealMaximum = Math.Max(
                    revealMaximum,
                    firstPostRevealFrameMilliseconds);
            }

            var receipt = new ScenarioReceipt
            {
                mode = scenario.Mode.ToString().ToLowerInvariant(),
                phase = DeferredPhase,
                generatedUtc = PsoFileUtility.UtcNowText(),
                completed = completed && scenario.IsContentCompleted,
                runDurationSeconds = RunDurationSeconds,
                contentRequestSeconds = ContentRequestDelaySeconds,
                contentRevealSeconds = revealStart,
                revealWindowEndSeconds = revealEnd,
                maximumMilliseconds = maximum,
                revealWindowMaximumMilliseconds = revealMaximum,
                firstPostRevealFrameMilliseconds = firstPostRevealFrameMilliseconds,
                severeFrameThresholdMilliseconds = SevereFrameMilliseconds,
                severeFrameCount = severeFrameCount,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                deferredReady = scenario.IsDeferredReady,
                deadlineMissed = scenario.DeadlineMissed,
                deferredReadySeconds = scenario.IsDeferredReady
                    ? Math.Max(
                        0.0,
                        scenario.DeferredReadyAt - scenario.WorkloadStartedAt)
                    : -1.0,
                frameSamples = scenarioFrameSamples.ToArray(),
            };
            PsoFileUtility.WriteJsonAtomic(scenarioReportPath, receipt);
        }

        private void OnDestroy()
        {
            WriteScenarioReceipt(runCompleteMarkerWritten);
            markerWriter?.Flush();
            for (int index = 0; index < materials.Count; index++)
                Destroy(materials[index]);
            if (white != null)
                Destroy(white);
        }
    }
}
