using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Yanagisawa.ShaderHitchPipeline.MegacityMetro
{
    [DefaultExecutionOrder(32000)]
    [DisallowMultipleComponent]
    public sealed class PsoMegacityMetroDeadlineAdapter : MonoBehaviour
    {
        [Serializable]
        private struct ScenarioFrameSample
        {
            public double elapsedSeconds;
            public double milliseconds;
            public bool nativeTimingAvailable;
            public double cpuTotalMilliseconds;
            public double cpuMainThreadMilliseconds;
            public double cpuMainThreadPresentWaitMilliseconds;
            public double cpuRenderThreadMilliseconds;
            public double gpuMilliseconds;
        }

        [Serializable]
        private sealed class ScenarioReceipt
        {
            public int schemaVersion = 6;
            public string scenario = "megacity-metro";
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
            public string qualityLevelName;
            public int outputWidth;
            public int outputHeight;
            public double internalRenderScale;
            public bool internalRenderScaleApplied;
            public double cameraFarClipPlane;
            public int controlledGraphicsStateCount;
            public bool deterministicCameraPass;
            public double simulationTimeScale;
            public string cameraMotion;
            public bool cameraMotionPreconditioned;
            public double cameraCircuitSeconds;
            public bool cameraCircuitCompletedBeforeMeasurement;
            public double cameraCircuitPhaseAtArmSeconds;
            public double cameraOriginWorldX;
            public double cameraOriginWorldY;
            public double cameraOriginWorldZ;
            public double cameraOriginRotationX;
            public double cameraOriginRotationY;
            public double cameraOriginRotationZ;
            public double cameraOriginRotationW;
            public bool captureConditioned;
            public double captureConditioningLeadSeconds;
            public bool startupQuiescenceRequired;
            public double startupQuiescenceRequiredSeconds;
            public double startupQuiescenceFrameCeilingMilliseconds;
            public double startupQuiescenceObservedSeconds;
            public int startupQuiescenceResetCount;
            public double controlledPreflightSeconds;
            public int preflightFrameSampleCount;
            public double preflightMeanMilliseconds;
            public double preflightP95Milliseconds;
            public double preflightMaximumMilliseconds;
            public int preflightNativeTimingSampleCount;
            public double preflightMaximumCpuTotalMilliseconds;
            public double preflightMeanCpuTotalMilliseconds;
            public double preflightMaximumCpuMainThreadMilliseconds;
            public double preflightMeanCpuMainThreadMilliseconds;
            public double preflightMaximumCpuMainThreadPresentWaitMilliseconds;
            public double preflightMeanCpuMainThreadPresentWaitMilliseconds;
            public double preflightMaximumCpuRenderThreadMilliseconds;
            public double preflightMeanCpuRenderThreadMilliseconds;
            public double preflightMaximumGpuMilliseconds;
            public double preflightMeanGpuMilliseconds;
            public int preflightManagedGcCollectionCountAtStart;
            public int preflightManagedGcCollectionCountAtEnd;
            public int preflightManagedGcCollections;
            public int preflightFramesAtOrAboveQuiescenceCeiling;
            public int preflightFramesAtOrAbovePresentationBudget;
            public bool diagnosticHudDisabled;
            public bool diagnosticStaticCamera;
            public double diagnosticFarClipPlane;
            public int diagnosticTargetFrameRate;
            public int targetFrameRate;
            public int vSyncCount;
            public bool frameTimingStatsEnabled;
            public int frameTimingSampleCount;
            public double maximumCpuTotalMilliseconds;
            public double maximumCpuMainThreadMilliseconds;
            public double maximumCpuMainThreadPresentWaitMilliseconds;
            public double maximumCpuRenderThreadMilliseconds;
            public double maximumGpuMilliseconds;
            public int managedGcCollectionCountAtStart;
            public int managedGcCollectionCountAtEnd;
            public int managedGcCollectionsDuringRun;
            public bool deferredReady;
            public bool deadlineMissed;
            public double deferredReadySeconds;
            public bool simulationSystemsFrozen;
            public int frozenSimulationWorldCount;
            public string[] frozenSimulationWorlds;
            public bool initializationSystemsFrozen;
            public int frozenInitializationWorldCount;
            public string[] frozenInitializationWorlds;
            public bool presentationSystemsRemainActive;
            public int activePresentationWorldCount;
            public string[] activePresentationWorlds;
            public ScenarioFrameSample[] frameSamples;
        }

        private sealed class FrozenSystemGroup
        {
            public World world;
            public ComponentSystemGroup group;
        }

        private const double RunDurationSeconds = 12.0;
        private const double ContentCompleteDelaySeconds = 2.0;
        private const double DistrictFreezeLeadSeconds = 0.25;
        private const float SevereFrameMilliseconds = 16.67f;
        private const int DeferredGpuStateCount = 48;
        private const int UniqueStatePresentationFrames = 2;
        private const int SteadyStateMaterialIndex = 2;
        private const int BenchmarkTargetFrameRate = 120;
        private const int BenchmarkOutputWidth = 1280;
        private const int BenchmarkOutputHeight = 720;
        private const float BenchmarkInternalRenderScale = 1.0f;
        private const float BenchmarkFarClipPlane = 150.0f;
        private const float ControlledSimulationTimeScale = 0.0f;
        private const double StartupQuiescenceRequiredSeconds = 3.0;
        private const double StartupQuiescenceFrameCeilingMilliseconds = 14.0;
        private const double StartupQuiescenceTimeoutSeconds = 60.0;
        private const double CameraCircuitSeconds = 12.0;
        private const int StartupConditioningCircuitCount = 2;
        private const double CameraMeasurementStartPhaseSeconds = 4.0;
        private const double CaptureStartupGraceSeconds = 0.5;
        private const float CameraForwardAmplitudeMeters = 7.0f;
        private const float CameraLateralAmplitudeMeters = 2.25f;
        private const float CameraVerticalAmplitudeMeters = 0.45f;
        private const float CameraYawAmplitudeDegrees = 2.4f;
        private const string BenchmarkQualityName = "Medium";
        private const string DiagnosticNoHudArgument =
            "-pso-megacity-diagnostic-no-hud";
        private const string DiagnosticStaticCameraArgument =
            "-pso-megacity-diagnostic-static-camera";
        private const string DiagnosticFarClipArgument =
            "-pso-megacity-diagnostic-far-clip";
        private const string DiagnosticTargetFrameRateArgument =
            "-pso-megacity-diagnostic-target-fps";
        private static int benchmarkQualityIndex = -1;

        private static readonly string[] StateKeywords =
        {
            "MC_WET",
            "MC_EMISSION",
            "MC_GRID",
            "MC_DECAL",
            "MC_GLASS",
            "MC_SHIELD",
            "MC_RAIN",
            "MC_HOLOGRAM",
            "MC_DAMAGE",
            "MC_DISTORT",
        };

        [SerializeField]
        private Camera benchmarkCamera;

        [SerializeField]
        private Vector3 controlledCameraOriginWorldPosition;

        [SerializeField]
        private Quaternion controlledCameraOriginWorldRotation = Quaternion.identity;

        [SerializeField]
        private bool controlledCameraOriginConfigured;

        [SerializeField]
        private Vector3 continuityWorldPosition;

        [SerializeField]
        private int deferredRendererCount;

        [SerializeField]
        private string phase = "megacity-metro-reveal";

        [SerializeField]
        private double requestDelaySeconds = 2.0;

        [SerializeField]
        private double deadlineSeconds = 2.5;

        [SerializeField]
        [Range(8, 31)]
        private int benchmarkLayer = 29;

        [SerializeField]
        private int startupTraceFrames = 18;

        [SerializeField]
        private Shader revealShader;

        [FormerlySerializedAs("force240Fps")]
        [SerializeField]
        private bool forceBenchmarkFramePacing = true;

        private readonly List<ScenarioFrameSample> frameSamples =
            new List<ScenarioFrameSample>(4096);
        private readonly List<Material> stateMaterials = new List<Material>();
        private readonly List<Renderer> stateRenderers = new List<Renderer>();
        private readonly List<FrozenSystemGroup> frozenSystemGroups =
            new List<FrozenSystemGroup>();
        private readonly List<string> frozenSimulationWorldNames =
            new List<string>();
        private readonly List<string> frozenInitializationWorldNames =
            new List<string>();
        private readonly List<string> activePresentationWorldNames =
            new List<string>();
        private readonly List<float> preflightFrameSamples =
            new List<float>(4096);
        private readonly FrameTiming[] latestFrameTimings = new FrameTiming[1];

        private PsoDeadlineScenario scenario;
        private PsoScenarioMarkerWriter markerWriter;
        private Texture2D white;
        private double readyAt = -1.0;
        private double lastSevereAt = -1.0;
        private double firstPostRevealFrameMilliseconds = -1.0;
        private float currentFrameMilliseconds;
        private float worstFrameMilliseconds;
        private int severeFrameCount;
        private bool revealLayerVisible;
        private bool revealDistrictFrozen;
        private bool stateRenderersConsolidated;
        private int uniqueStatePresentationFrameCount;
        private bool deferredTrainingTraceEnded;
        private bool runComplete;
        private bool scenarioReceiptWritten;
        private bool controlledFlightStarted;
        private float originalTimeScale;
        private int originalTargetFrameRate;
        private int originalVSyncCount;
        private Vector3 controlledCameraStartPosition;
        private Quaternion controlledCameraStartRotation;
        private bool frameTimingStatsEnabled;
        private double controlledFlightStartedAt = -1.0;
        private double startupConditioningBeganAt = -1.0;
        private double startupQuiescenceStartedAt = -1.0;
        private double startupQuiescenceObservedSeconds;
        private int startupQuiescenceResetCount;
        private double previousCameraCircuitPhase = -1.0;
        private double captureReadyAt = -1.0;
        private double cameraCircuitPhaseAtArm = -1.0;
        private bool cameraCircuitCompletedBeforeMeasurement;
        private bool captureReadyAnnounced;
        private bool startupFailureReported;
        private bool measurementGcPreparedForArm;
        private bool traceRequested;
        private RenderPipelineAsset benchmarkRenderPipelineAsset;
        private PropertyInfo benchmarkRenderScaleProperty;
        private float originalRenderScale = 1.0f;
        private float appliedRenderScale = 1.0f;
        private bool renderScaleApplied;
        private double preflightFrameTotalMilliseconds;
        private double preflightMaximumMilliseconds;
        private int preflightNativeTimingSampleCount;
        private double preflightCpuTotalMilliseconds;
        private double preflightMaximumCpuTotalMilliseconds;
        private double preflightCpuMainThreadMilliseconds;
        private double preflightMaximumCpuMainThreadMilliseconds;
        private double preflightCpuMainThreadPresentWaitMilliseconds;
        private double preflightMaximumCpuMainThreadPresentWaitMilliseconds;
        private double preflightCpuRenderThreadMilliseconds;
        private double preflightMaximumCpuRenderThreadMilliseconds;
        private double preflightGpuMilliseconds;
        private double preflightMaximumGpuMilliseconds;
        private int preflightManagedGcCollectionCountAtStart = -1;
        private bool diagnosticHudDisabled;
        private bool diagnosticStaticCamera;
        private float diagnosticFarClipPlane;
        private float originalFarClipPlane;
        private float appliedFarClipPlane;
        private int diagnosticTargetFrameRate;
        private int effectiveTargetFrameRate = BenchmarkTargetFrameRate;
        private int preflightFramesAtOrAboveQuiescenceCeiling;
        private int preflightFramesAtOrAbovePresentationBudget;
        private int managedGcCollectionCountAtStart = -1;
        private int managedGcCollectionCountAtEnd = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void ApplyBenchmarkQualityBeforePipelineBootstrap()
        {
            TryApplyBenchmarkQuality();
        }

        private static bool TryApplyBenchmarkQuality()
        {
            if (benchmarkQualityIndex < 0)
            {
                string[] names = QualitySettings.names;
                benchmarkQualityIndex = Array.FindIndex(
                    names,
                    item => string.Equals(
                        item,
                        BenchmarkQualityName,
                        StringComparison.Ordinal));
            }
            if (benchmarkQualityIndex < 0)
                return false;
            int current = QualitySettings.GetQualityLevel();
            if (current == benchmarkQualityIndex)
                return true;
            QualitySettings.SetQualityLevel(benchmarkQualityIndex, true);
            return QualitySettings.GetQualityLevel() == benchmarkQualityIndex;
        }

        private void EnforceBenchmarkQuality()
        {
            if (benchmarkQualityIndex < 0 && !TryApplyBenchmarkQuality())
            {
                throw new InvalidOperationException(
                    "Megacity benchmark quality level was not found: " +
                    BenchmarkQualityName);
            }
            int current = QualitySettings.GetQualityLevel();
            if (current == benchmarkQualityIndex)
                return;
            QualitySettings.SetQualityLevel(benchmarkQualityIndex, true);
            if (QualitySettings.GetQualityLevel() != benchmarkQualityIndex)
                throw new InvalidOperationException(
                    "Megacity benchmark could not restore quality level: " +
                    BenchmarkQualityName);
            Debug.Log(
                "[ShaderHitchPipeline.MegacityMetro] Restored benchmark " +
                "quality after project-side drift: " + BenchmarkQualityName);
        }
        private int originalCameraCullingMask;
        private string scenarioReportPath;
        private Color modeColor;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle statusStyle;
        private GUIStyle hitchStyle;
        private string titleText;
        private string detailText;
        private GUIContent titleContent;
        private GUIContent detailContent;
        private GUIContent waitingStatusContent;
        private GUIContent revealedStatusContent;
        private GUIContent currentStatusContent;
        private GUIContent footerDurationContent;
        private GUIContent footerPresentContent;
        private GUIContent footerWorstContent;
        private GUIContent footerMissedContent;
        private GUIContent alertActualContent;
        private GUIContent alertFrameContent;
        private GUIContent[] requestStatusContents;
        private GUIContent[] revealStatusContents;
        private GUIContent[] elapsedTimeContents;
        private GUIContent[] frameTimeContents;
        private GUIContent[] severeCountContents;
        private GUIContent currentElapsedContent;
        private GUIContent currentFrameContent;
        private GUIContent currentHitchFrameContent;
        private GUIContent currentWorstContent;
        private GUIContent currentSevereCountContent;
        private double nextHudTextRefreshAt;
        private Transform revealDistrictRoot;
        private Mesh steadyStateMesh;
        private Renderer steadyStateRenderer;

        public void Configure(
            Camera camera,
            Vector3 targetWorldPosition,
            int selectedRendererCount,
            int layer,
            Shader shader = null)
        {
            benchmarkCamera = camera;
            controlledCameraOriginWorldPosition = camera.transform.position;
            controlledCameraOriginWorldRotation = camera.transform.rotation;
            controlledCameraOriginConfigured = true;
            continuityWorldPosition = targetWorldPosition;
            deferredRendererCount = selectedRendererCount;
            benchmarkLayer = layer;
            if (shader != null)
                revealShader = shader;
        }

        private void Awake()
        {
            originalTimeScale = Time.timeScale;
            originalTargetFrameRate = Application.targetFrameRate;
            originalVSyncCount = QualitySettings.vSyncCount;
            traceRequested = PsoCommandLine.Current.HasFlag(
                PsoConstants.TraceArgument);
            EnforceBenchmarkQuality();
            ApplyBenchmarkRenderScale();
            Screen.SetResolution(
                BenchmarkOutputWidth,
                BenchmarkOutputHeight,
                FullScreenMode.Windowed);
            if (benchmarkCamera == null)
                benchmarkCamera = Camera.main;
            if (benchmarkCamera == null)
                throw new InvalidOperationException("Megacity benchmark requires a camera.");
            diagnosticHudDisabled = PsoCommandLine.Current.HasFlag(
                DiagnosticNoHudArgument);
            diagnosticStaticCamera = PsoCommandLine.Current.HasFlag(
                DiagnosticStaticCameraArgument);
            diagnosticFarClipPlane = (float)PsoCommandLine.Current.GetDouble(
                DiagnosticFarClipArgument,
                0.0,
                0.0,
                10000.0);
            diagnosticTargetFrameRate = PsoCommandLine.Current.GetInt(
                DiagnosticTargetFrameRateArgument,
                0,
                0,
                1000);
            effectiveTargetFrameRate = diagnosticTargetFrameRate > 0
                ? diagnosticTargetFrameRate
                : BenchmarkTargetFrameRate;
            originalFarClipPlane = benchmarkCamera.farClipPlane;
            benchmarkCamera.farClipPlane = diagnosticFarClipPlane > 0.0f
                ? diagnosticFarClipPlane
                : BenchmarkFarClipPlane;
            appliedFarClipPlane = benchmarkCamera.farClipPlane;
            if (!controlledCameraOriginConfigured)
            {
                controlledCameraOriginWorldPosition = benchmarkCamera.transform.position;
                controlledCameraOriginWorldRotation = benchmarkCamera.transform.rotation;
                controlledCameraOriginConfigured = true;
            }
            controlledCameraStartPosition = controlledCameraOriginWorldPosition;
            controlledCameraStartRotation = controlledCameraOriginWorldRotation;
            if (deferredRendererCount <= 0)
            {
                throw new InvalidOperationException(
                    "Megacity benchmark requires a baked upcoming renderer set.");
            }
            if (forceBenchmarkFramePacing)
                EnforceFrameRate();
            Application.runInBackground = true;
            frameTimingStatsEnabled = FrameTimingManager.IsFeatureEnabled();
            if (frameTimingStatsEnabled)
                FrameTimingManager.CaptureFrameTimings();

            markerWriter = new PsoScenarioMarkerWriter("MegacityMetro");
            scenarioReportPath = PsoCommandLine.Current.GetString(
                PsoConstants.ScenarioReportArgument,
                string.Empty);
            scenario = new PsoDeadlineScenario(
                phase,
                "megacity-metro",
                requestDelaySeconds,
                deadlineSeconds,
                startupTraceFrames,
                markerWriter.Write);
            switch (scenario.Mode)
            {
                case PsoDeadlineScenarioMode.Baseline:
                    modeColor = new Color(1.0f, 0.28f, 0.22f);
                    titleText = "MEGACITY METRO · UNITY DEFAULT / COLD";
                    break;
                case PsoDeadlineScenarioMode.Throughput:
                    modeColor = new Color(1.0f, 0.68f, 0.18f);
                    titleText = "MEGACITY METRO · UNITY WARM ALL NOW";
                    break;
                default:
                    modeColor = new Color(0.18f, 0.94f, 0.68f);
                    titleText = "MEGACITY METRO · OURS / DEADLINE SCHEDULED";
                    break;
            }
            detailText = deferredRendererCount +
                         " real city renderers + " + DeferredGpuStateCount +
                         " first-use GPU states · phase " + phase;
            InitializeHudContent();
            originalCameraCullingMask = benchmarkCamera.cullingMask;
            // The editor binder stamps this layer on authoring renderers before
            // Entities Graphics baking. Runtime reveal is therefore one camera-mask
            // change, not a mass ECS structural change in the measured frame.
            benchmarkCamera.cullingMask &= ~(1 << benchmarkLayer);
            BuildDeferredGpuStateWorkload();

            white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            double delay = PsoCommandLine.Current.GetDouble(
                PsoConstants.BenchmarkDelayArgument,
                1.0,
                0.0,
                3600.0);
            // Capture and freeze the authoring camera at the same wall-clock seam
            // in every arm. Warmup duration must not select a different city block
            // for baseline, throughput, and deadline-scheduled measurements.
            readyAt = Time.realtimeSinceStartupAsDouble + delay;
        }

        private void Update()
        {
            // Megacity's settings UI restores persisted user preferences after
            // scene startup. Keep quality an explicit benchmark invariant.
            EnforceBenchmarkQuality();
            double now = Time.realtimeSinceStartupAsDouble;
            if (runComplete)
            {
                TryQuitAfterMeasurement();
                return;
            }
            // Megacity streams and creates stock Entities Graphics states during
            // startup. Keep those in the startup hot set; begin the deferred trace
            // only after the content request, then retain a short stabilization
            // window before the controlled district first draw. This prevents the
            // reveal plan from absorbing unrelated streaming-state GUIDs.
            if (traceRequested && scenario.IsContentRequested)
                scenario.AdvanceTrainingTrace(now);
            if (!scenario.IsArmed)
            {
                if (now < readyAt)
                    return;
                if (!BenchmarkResolutionReady())
                    return;
                BeginControlledFlight(now);
                if (!scenario.IsStartupReady)
                    return;
                if (!measurementGcPreparedForArm)
                {
                    if (!StartupQuiescenceReady(now))
                        return;
                    // Capture startup can spend several circuits retrying the
                    // strict stability window. Reset the managed nursery at the
                    // actual T0 seam, then wait one complete frame before opening
                    // either sampler so the collection pause cannot enter timing.
                    PrepareManagedHeapForMeasurement();
                    measurementGcPreparedForArm = true;
                    return;
                }
                double armNow = Time.realtimeSinceStartupAsDouble;
                cameraCircuitPhaseAtArm = CurrentCameraCircuitPhase(armNow);
                managedGcCollectionCountAtStart = GC.CollectionCount(0);
                scenario.Arm(armNow);
                now = armNow;
            }

            currentFrameMilliseconds = Time.unscaledDeltaTime * 1000.0f;
            FrameTiming nativeTiming = default;
            bool nativeTimingAvailable = frameTimingStatsEnabled &&
                FrameTimingManager.GetLatestTimings(1, latestFrameTimings) > 0;
            if (nativeTimingAvailable)
                nativeTiming = latestFrameTimings[0];
            double elapsed = Math.Max(0.0, now - scenario.WorkloadStartedAt);
            if (elapsed <= RunDurationSeconds + 0.25)
            {
                frameSamples.Add(new ScenarioFrameSample
                {
                    elapsedSeconds = elapsed,
                    milliseconds = currentFrameMilliseconds,
                    nativeTimingAvailable = nativeTimingAvailable,
                    cpuTotalMilliseconds = ValidTiming(
                        nativeTiming.cpuFrameTime,
                        nativeTimingAvailable),
                    cpuMainThreadMilliseconds = ValidTiming(
                        nativeTiming.cpuMainThreadFrameTime,
                        nativeTimingAvailable),
                    cpuMainThreadPresentWaitMilliseconds = ValidTiming(
                        nativeTiming.cpuMainThreadPresentWaitTime,
                        nativeTimingAvailable),
                    cpuRenderThreadMilliseconds = ValidTiming(
                        nativeTiming.cpuRenderThreadFrameTime,
                        nativeTimingAvailable),
                    gpuMilliseconds = ValidTiming(
                        nativeTiming.gpuFrameTime,
                        nativeTimingAvailable),
                });
                if (revealLayerVisible && firstPostRevealFrameMilliseconds < 0.0)
                    firstPostRevealFrameMilliseconds = currentFrameMilliseconds;
                worstFrameMilliseconds = Mathf.Max(
                    worstFrameMilliseconds,
                    currentFrameMilliseconds);
                if (currentFrameMilliseconds >= SevereFrameMilliseconds)
                {
                    severeFrameCount++;
                    lastSevereAt = now;
                    currentHitchFrameContent = frameTimeContents[ClampLookupIndex(
                        currentFrameMilliseconds,
                        10.0,
                        frameTimeContents.Length)];
                }
            }

            scenario.Tick(now);
            if (scenario.IsContentRevealed &&
                !scenario.IsContentCompleted &&
                !revealLayerVisible)
                RevealUpcomingRenderers();
            AdvanceFirstUseStateBurst();
            if (scenario.IsContentRevealed &&
                !scenario.IsContentCompleted &&
                now >= scenario.ContentRevealAt + ContentCompleteDelaySeconds)
            {
                scenario.MarkContentComplete(now);
            }
            // Unity 6000.1 can defer a graphics-state payload well beyond the
            // first visible frame. Keep tracing through the declared content
            // completion boundary, then let the merge-time shader allowlist
            // remove every unrelated background-city state.
            if (!deferredTrainingTraceEnded && scenario.IsContentCompleted)
            {
                deferredTrainingTraceEnded = true;
                PsoTraceController trace = traceRequested
                    ? PsoTraceController.Instance
                    : null;
                if (trace != null && trace.IsTracing &&
                    string.Equals(
                        trace.ActivePhase,
                        phase,
                        StringComparison.OrdinalIgnoreCase))
                {
                    trace.EndPhase();
                    markerWriter.Write("TRACE_DEFERRED_END", now);
                }
            }
            if (scenario.IsContentCompleted && revealLayerVisible)
                HideCompletedRenderers();
            if (!runComplete && elapsed >= RunDurationSeconds)
            {
                runComplete = true;
                managedGcCollectionCountAtEnd = GC.CollectionCount(0);
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

        private void LateUpdate()
        {
            // Catch any later gameplay callback before the render loop consumes
            // a different pipeline asset or quality level.
            EnforceBenchmarkQuality();

            // Megacity's RateSettings may run after scene Awake and restore its
            // own presentation policy. Reassert the benchmark contract after all
            // gameplay systems so stable 60 Hz pacing is not misclassified as a
            // PSO deadline miss.
            if (forceBenchmarkFramePacing)
                EnforceFrameRate();

            if (frameTimingStatsEnabled)
                FrameTimingManager.CaptureFrameTimings();

            UpdateControlledFlight(Time.realtimeSinceStartupAsDouble);

            // Keep the pre-created district in the live camera's view while the
            // vehicle/camera bootstrap settles. Freeze the one common parent a
            // quarter-second before reveal, outside the measured first-draw
            // frame. The reveal itself therefore performs only the camera-mask
            // flip; it does not instantiate or reposition 48 renderers.
            if (!revealDistrictFrozen &&
                scenario != null &&
                scenario.IsArmed &&
                Time.realtimeSinceStartupAsDouble >=
                    scenario.ContentRevealAt - DistrictFreezeLeadSeconds &&
                revealDistrictRoot != null)
            {
                revealDistrictRoot.SetParent(null, true);
                revealDistrictFrozen = true;
            }

        }

        private void BeginControlledFlight(double now)
        {
            if (controlledFlightStarted)
                return;

            controlledFlightStarted = true;
            controlledFlightStartedAt = now;
            Time.timeScale = ControlledSimulationTimeScale;
            benchmarkCamera.transform.SetPositionAndRotation(
                controlledCameraStartPosition,
                controlledCameraStartRotation);
            PrepareManagedHeapForMeasurement();
            markerWriter.Write("CONTROLLED_FLIGHT_BEGIN", now);
        }

        private bool StartupQuiescenceReady(double now)
        {
            if (startupConditioningBeganAt < 0.0)
                startupConditioningBeganAt = now;

            double phase = CurrentCameraCircuitPhase(now);
            double previousPhase = previousCameraCircuitPhase;
            bool wrapped = previousPhase >= 0.0 && phase < previousPhase;
            bool crossedMeasurementPhase = previousPhase >= 0.0 &&
                previousPhase < CameraMeasurementStartPhaseSeconds &&
                phase >= CameraMeasurementStartPhaseSeconds;
            previousCameraCircuitPhase = phase;

            // Active Megacity Simulation is intentionally allowed to exceed the
            // final 60 Hz budget while SubScenes and ECS state converge. The
            // strict 14 ms window starts only after those non-presentation
            // groups are frozen; requiring it before the freeze is a circular
            // condition on CPU-bound city workloads.
            double frameMilliseconds = Time.unscaledDeltaTime * 1000.0;
            if (captureReadyAnnounced)
                RecordPreflightFrame(frameMilliseconds);
            if (captureReadyAnnounced &&
                frameMilliseconds > StartupQuiescenceFrameCeilingMilliseconds)
            {
                if (startupQuiescenceStartedAt >= 0.0)
                    startupQuiescenceResetCount++;
                startupQuiescenceStartedAt = -1.0;
                startupQuiescenceObservedSeconds = 0.0;
            }
            else if (captureReadyAnnounced)
            {
                if (startupQuiescenceStartedAt < 0.0)
                    startupQuiescenceStartedAt = now;
                startupQuiescenceObservedSeconds =
                    now - startupQuiescenceStartedAt;
            }

            double controlledElapsed = now - controlledFlightStartedAt;
            if (!captureReadyAnnounced &&
                controlledElapsed >=
                    CameraCircuitSeconds * StartupConditioningCircuitCount &&
                wrapped)
            {
                cameraCircuitCompletedBeforeMeasurement = true;
                captureReadyAnnounced = true;
                // Exercise two complete real-scene circuits so SubScenes and
                // stock Entities Graphics states converge before the common
                // freeze seam. Capture begins after the freeze, and only a new
                // continuous 3-second <14 ms proof may arm measurement.
                FreezeNonPresentationSystemGroups();
                PrepareManagedHeapForMeasurement();
                preflightManagedGcCollectionCountAtStart = GC.CollectionCount(0);
                captureReadyAt = Time.realtimeSinceStartupAsDouble;
                ResetQuiescenceWindow();
                markerWriter.Write("CAPTURE_READY", captureReadyAt);
                return false;
            }

            if (captureReadyAnnounced &&
                now - captureReadyAt >= CaptureStartupGraceSeconds &&
                crossedMeasurementPhase &&
                startupQuiescenceObservedSeconds >=
                    StartupQuiescenceRequiredSeconds)
            {
                markerWriter.Write(
                    "STARTUP_QUIESCENT:" + startupQuiescenceResetCount,
                    now);
                return true;
            }

            if (now - startupConditioningBeganAt > StartupQuiescenceTimeoutSeconds)
            {
                FailStartupConditioning(now);
            }
            return false;
        }

        private void FailStartupConditioning(double now)
        {
            if (startupFailureReported)
                return;
            startupFailureReported = true;
            string message =
                "Megacity did not reach the required post-freeze startup " +
                "quiescence: " +
                StartupQuiescenceRequiredSeconds.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) +
                " continuous seconds below " +
                StartupQuiescenceFrameCeilingMilliseconds.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) + " ms; observed mean=" +
                PreflightMeanMilliseconds().ToString(
                    "F2",
                    CultureInfo.InvariantCulture) + " ms, p95=" +
                Percentile(preflightFrameSamples, 0.95).ToString(
                    "F2",
                    CultureInfo.InvariantCulture) + " ms, max=" +
                preflightMaximumMilliseconds.ToString(
                    "F2",
                    CultureInfo.InvariantCulture) + " ms, native CPU=" +
                preflightMaximumCpuTotalMilliseconds.ToString(
                    "F2",
                    CultureInfo.InvariantCulture) + " ms, PresentWait=" +
                preflightMaximumCpuMainThreadPresentWaitMilliseconds.ToString(
                    "F2",
                    CultureInfo.InvariantCulture) + " ms, GPU=" +
                preflightMaximumGpuMilliseconds.ToString(
                    "F2",
                    CultureInfo.InvariantCulture) + " ms.";
            Debug.LogError(message);
            markerWriter.Write("SCENARIO_ERROR:" + message, now);
            WriteScenarioReceipt(false);
            markerWriter.Flush();
            Application.Quit(2);
        }

        private void RecordPreflightFrame(double frameMilliseconds)
        {
            currentFrameMilliseconds = (float)frameMilliseconds;
            if (preflightFrameSamples.Count < preflightFrameSamples.Capacity)
            {
                preflightFrameSamples.Add((float)frameMilliseconds);
                preflightFrameTotalMilliseconds += frameMilliseconds;
            }
            preflightMaximumMilliseconds = Math.Max(
                preflightMaximumMilliseconds,
                frameMilliseconds);
            if (frameMilliseconds >= StartupQuiescenceFrameCeilingMilliseconds)
                preflightFramesAtOrAboveQuiescenceCeiling++;
            if (frameMilliseconds >= SevereFrameMilliseconds)
                preflightFramesAtOrAbovePresentationBudget++;

            bool timingAvailable = frameTimingStatsEnabled &&
                FrameTimingManager.GetLatestTimings(1, latestFrameTimings) > 0;
            if (!timingAvailable)
                return;
            FrameTiming timing = latestFrameTimings[0];
            preflightNativeTimingSampleCount++;
            double cpuTotal = ValidTiming(timing.cpuFrameTime, true);
            double cpuMain = ValidTiming(timing.cpuMainThreadFrameTime, true);
            double presentWait = ValidTiming(
                timing.cpuMainThreadPresentWaitTime,
                true);
            double renderThread = ValidTiming(
                timing.cpuRenderThreadFrameTime,
                true);
            double gpu = ValidTiming(timing.gpuFrameTime, true);
            preflightCpuTotalMilliseconds += cpuTotal;
            preflightCpuMainThreadMilliseconds += cpuMain;
            preflightCpuMainThreadPresentWaitMilliseconds += presentWait;
            preflightCpuRenderThreadMilliseconds += renderThread;
            preflightGpuMilliseconds += gpu;
            preflightMaximumCpuTotalMilliseconds = Math.Max(
                preflightMaximumCpuTotalMilliseconds,
                cpuTotal);
            preflightMaximumCpuMainThreadMilliseconds = Math.Max(
                preflightMaximumCpuMainThreadMilliseconds,
                cpuMain);
            preflightMaximumCpuMainThreadPresentWaitMilliseconds = Math.Max(
                preflightMaximumCpuMainThreadPresentWaitMilliseconds,
                presentWait);
            preflightMaximumCpuRenderThreadMilliseconds = Math.Max(
                preflightMaximumCpuRenderThreadMilliseconds,
                renderThread);
            preflightMaximumGpuMilliseconds = Math.Max(
                preflightMaximumGpuMilliseconds,
                gpu);
        }

        private double PreflightMeanMilliseconds()
        {
            return preflightFrameSamples.Count == 0
                ? 0.0
                : preflightFrameTotalMilliseconds / preflightFrameSamples.Count;
        }

        private double MeanNativeTiming(double totalMilliseconds)
        {
            return preflightNativeTimingSampleCount == 0
                ? 0.0
                : totalMilliseconds / preflightNativeTimingSampleCount;
        }

        private static double Percentile(List<float> values, double percentile)
        {
            if (values.Count == 0)
                return 0.0;
            float[] ordered = values.ToArray();
            Array.Sort(ordered);
            int index = Mathf.Clamp(
                Mathf.CeilToInt((float)(percentile * ordered.Length)) - 1,
                0,
                ordered.Length - 1);
            return ordered[index];
        }

        private static bool BenchmarkResolutionReady()
        {
            if (Screen.width == BenchmarkOutputWidth &&
                Screen.height == BenchmarkOutputHeight)
            {
                return true;
            }
            Screen.SetResolution(
                BenchmarkOutputWidth,
                BenchmarkOutputHeight,
                FullScreenMode.Windowed);
            return false;
        }

        private void ApplyBenchmarkRenderScale()
        {
            benchmarkRenderPipelineAsset = QualitySettings.renderPipeline;
            if (benchmarkRenderPipelineAsset == null)
                benchmarkRenderPipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (benchmarkRenderPipelineAsset == null)
            {
                throw new InvalidOperationException(
                    "Megacity benchmark requires an active render-pipeline asset.");
            }

            benchmarkRenderScaleProperty = benchmarkRenderPipelineAsset.GetType().GetProperty(
                "renderScale",
                BindingFlags.Instance | BindingFlags.Public);
            if (benchmarkRenderScaleProperty == null ||
                !benchmarkRenderScaleProperty.CanRead ||
                !benchmarkRenderScaleProperty.CanWrite)
            {
                throw new InvalidOperationException(
                    "Megacity benchmark could not control the URP render scale.");
            }

            originalRenderScale = Convert.ToSingle(
                benchmarkRenderScaleProperty.GetValue(benchmarkRenderPipelineAsset),
                CultureInfo.InvariantCulture);
            benchmarkRenderScaleProperty.SetValue(
                benchmarkRenderPipelineAsset,
                BenchmarkInternalRenderScale);
            appliedRenderScale = Convert.ToSingle(
                benchmarkRenderScaleProperty.GetValue(benchmarkRenderPipelineAsset),
                CultureInfo.InvariantCulture);
            renderScaleApplied = Math.Abs(
                appliedRenderScale - BenchmarkInternalRenderScale) <= 0.001f;
            if (!renderScaleApplied)
            {
                throw new InvalidOperationException(
                    "Megacity benchmark failed to apply its pinned URP render scale.");
            }
            Debug.Log(
                "[ShaderHitchPipeline.MegacityMetro] Pinned URP render scale=" +
                appliedRenderScale.ToString("F2", CultureInfo.InvariantCulture));
        }

        private void ResetQuiescenceWindow()
        {
            startupQuiescenceStartedAt = -1.0;
            startupQuiescenceObservedSeconds = 0.0;
        }

        private static void PrepareManagedHeapForMeasurement()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private double CurrentCameraCircuitPhase(double now)
        {
            double elapsed = Math.Max(0.0, now - controlledFlightStartedAt);
            double phase = elapsed % CameraCircuitSeconds;
            return phase < 0.0 ? phase + CameraCircuitSeconds : phase;
        }

        private void FreezeNonPresentationSystemGroups()
        {
            frozenSystemGroups.Clear();
            frozenSimulationWorldNames.Clear();
            frozenInitializationWorldNames.Clear();
            activePresentationWorldNames.Clear();
            foreach (World world in World.All)
            {
                if (world == null || !world.IsCreated)
                    continue;

                InitializationSystemGroup initialization =
                    world.GetExistingSystemManaged<InitializationSystemGroup>();
                if (initialization != null && initialization.Enabled)
                {
                    initialization.Enabled = false;
                    frozenSystemGroups.Add(new FrozenSystemGroup
                    {
                        world = world,
                        group = initialization,
                    });
                    frozenInitializationWorldNames.Add(world.Name);
                }

                SimulationSystemGroup simulation =
                    world.GetExistingSystemManaged<SimulationSystemGroup>();
                if (simulation != null && simulation.Enabled)
                {
                    simulation.Enabled = false;
                    frozenSystemGroups.Add(new FrozenSystemGroup
                    {
                        world = world,
                        group = simulation,
                    });
                    frozenSimulationWorldNames.Add(world.Name);
                }

                PresentationSystemGroup presentation =
                    world.GetExistingSystemManaged<PresentationSystemGroup>();
                if (presentation != null && presentation.Enabled)
                    activePresentationWorldNames.Add(world.Name);
            }
            frozenSimulationWorldNames.Sort(StringComparer.Ordinal);
            frozenInitializationWorldNames.Sort(StringComparer.Ordinal);
            activePresentationWorldNames.Sort(StringComparer.Ordinal);
            markerWriter.Write(
                "INITIALIZATION_SYSTEMS_FROZEN:" +
                frozenInitializationWorldNames.Count,
                Time.realtimeSinceStartupAsDouble);
            markerWriter.Write(
                "SIMULATION_SYSTEMS_FROZEN:" +
                frozenSimulationWorldNames.Count,
                Time.realtimeSinceStartupAsDouble);
            markerWriter.Write(
                "PRESENTATION_SYSTEMS_ACTIVE:" +
                activePresentationWorldNames.Count,
                Time.realtimeSinceStartupAsDouble);
        }

        private void RestoreFrozenSystemGroups()
        {
            for (int index = 0; index < frozenSystemGroups.Count; index++)
            {
                FrozenSystemGroup frozen = frozenSystemGroups[index];
                if (frozen.world != null &&
                    frozen.world.IsCreated &&
                    frozen.group != null)
                {
                    frozen.group.Enabled = true;
                }
            }
            frozenSystemGroups.Clear();
        }

        private static double ValidTiming(double value, bool available)
        {
            if (!available || double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;
            return Math.Max(0.0, value);
        }

        private void UpdateControlledFlight(double now)
        {
            if (!controlledFlightStarted || scenario == null)
                return;

            // Fly the exact same deterministic path during the quiescence gate.
            // T+0 therefore begins from a moving, already-converged city view;
            // starting the measurement does not itself trigger LOD/culling work.
            double phaseSeconds = scenario.IsArmed
                ? CameraMeasurementStartPhaseSeconds +
                  Math.Max(0.0, now - scenario.WorkloadStartedAt)
                : CurrentCameraCircuitPhase(now);
            if (diagnosticStaticCamera)
                phaseSeconds = CameraMeasurementStartPhaseSeconds;
            float phase = (float)(
                (phaseSeconds % CameraCircuitSeconds) *
                (Math.PI * 2.0 / CameraCircuitSeconds));
            float lateral = Mathf.Sin(phase) *
                            CameraLateralAmplitudeMeters;
            float vertical = Mathf.Sin(phase * 2.0f) *
                             CameraVerticalAmplitudeMeters;
            float forward = (1.0f - Mathf.Cos(phase)) *
                            CameraForwardAmplitudeMeters;
            Vector3 localOffset = new Vector3(lateral, vertical, forward);
            benchmarkCamera.transform.SetPositionAndRotation(
                controlledCameraStartPosition +
                controlledCameraStartRotation * localOffset,
                controlledCameraStartRotation * Quaternion.Euler(
                    0.0f,
                    Mathf.Sin(phase) * CameraYawAmplitudeDegrees,
                    0.0f));
        }

        private void EnforceFrameRate()
        {
            if (QualitySettings.vSyncCount != 0)
                QualitySettings.vSyncCount = 0;
            // Keep the producer at 2x the 60 FPS acceptance boundary. A 240 Hz
            // producer overloads Megacity's CPU-side Entities presentation and
            // creates queue oscillation even while native GPU time stays below
            // three milliseconds; the pinned 120 Hz cadence leaves deterministic
            // submission and desktop-composition headroom.
            if (Application.targetFrameRate != effectiveTargetFrameRate)
                Application.targetFrameRate = effectiveTargetFrameRate;
        }

        private void BuildDeferredGpuStateWorkload()
        {
            Shader shader = revealShader != null
                ? revealShader
                : Shader.Find("Yanagisawa/Megacity Metro PSO Reveal");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Megacity PSO reveal shader was not included in the build.");
            }

            var root = new GameObject("Megacity Deferred GPU State District");
            root.layer = benchmarkLayer;
            revealDistrictRoot = root.transform;
            // Camera-local placement survives Megacity's late player-camera
            // initialization. The district is detached at CONTENT_REQUEST so the
            // revealed geometry remains a world-space part of the city flight.
            revealDistrictRoot.SetParent(benchmarkCamera.transform, false);
            const int columnCount = 16;
            int rowCount = (DeferredGpuStateCount + columnCount - 1) / columnCount;
            var steadyStateParts = new CombineInstance[DeferredGpuStateCount];
            for (int index = 0; index < DeferredGpuStateCount; index++)
            {
                int column = index % columnCount;
                int row = index / columnCount;
                float lateral = (column - 7.5f) * 5.5f;
                float vertical = (row - (rowCount - 1) * 0.5f) * 5.0f;
                const float distance = 120.0f;
                Color color = Color.HSVToRGB(
                    Mathf.Repeat(0.48f + index * 0.0137f, 1.0f),
                    0.62f + (index % 4) * 0.08f,
                    0.78f + (index % 3) * 0.10f);
                Material material = CreateStateMaterial(shader, index, color);
                GameObject relay = GameObject.CreatePrimitive(PrimitiveType.Cube);
                relay.name = "Deferred District Relay " + index;
                relay.layer = benchmarkLayer;
                relay.transform.SetParent(revealDistrictRoot, false);
                relay.transform.localPosition = new Vector3(lateral, vertical, distance);
                relay.transform.localRotation = Quaternion.identity;
                relay.transform.localScale = new Vector3(
                    2.1f,
                    1.8f,
                    0.18f + (index % 3) * 0.08f);
                Renderer renderer = relay.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                stateRenderers.Add(renderer);
                MeshFilter meshFilter = relay.GetComponent<MeshFilter>();
                steadyStateParts[index] = new CombineInstance
                {
                    mesh = meshFilter.sharedMesh,
                    transform = Matrix4x4.TRS(
                        relay.transform.localPosition,
                        relay.transform.localRotation,
                        relay.transform.localScale),
                };
                Collider collider = relay.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
            }

            var steadyStateObject = new GameObject("Megacity Deferred District Steady State");
            steadyStateObject.layer = benchmarkLayer;
            steadyStateObject.transform.SetParent(revealDistrictRoot, false);
            steadyStateMesh = new Mesh
            {
                name = "Megacity Deferred District Combined Mesh",
                indexFormat = IndexFormat.UInt32,
            };
            steadyStateMesh.CombineMeshes(steadyStateParts, true, true, false);
            steadyStateObject.AddComponent<MeshFilter>().sharedMesh = steadyStateMesh;
            steadyStateRenderer = steadyStateObject.AddComponent<MeshRenderer>();
            steadyStateRenderer.sharedMaterial = stateMaterials[SteadyStateMaterialIndex];
            steadyStateRenderer.enabled = false;
        }

        private Material CreateStateMaterial(Shader shader, int stateIndex, Color color)
        {
            var material = new Material(shader)
            {
                name = "Megacity Deferred PSO " + stateIndex,
                enableInstancing = true,
            };
            for (int bit = 0; bit < StateKeywords.Length; bit++)
            {
                if ((stateIndex & (1 << bit)) != 0)
                    material.EnableKeyword(StateKeywords[bit]);
            }

            bool transparent = stateIndex % 5 == 0 || stateIndex % 11 == 0;
            bool additive = stateIndex % 7 == 0;
            material.renderQueue = transparent || additive ? 3000 : 2000;
            material.SetColor("_BaseColor", new Color(
                color.r,
                color.g,
                color.b,
                transparent ? 0.28f : additive ? 0.58f : 0.92f));
            material.SetColor(
                "_EmissionColor",
                Color.Lerp(color, Color.white, 0.28f) * 1.45f);
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
            stateMaterials.Add(material);
            return material;
        }

        private void RevealUpcomingRenderers()
        {
            revealLayerVisible = true;
            benchmarkCamera.cullingMask |= 1 << benchmarkLayer;
        }

        private void AdvanceFirstUseStateBurst()
        {
            if (!revealLayerVisible || stateRenderersConsolidated)
                return;

            // A training trace must retain all unique render states until Unity
            // publishes the final GraphicsStateCollection payload. Runtime
            // comparisons only need two genuinely presented first-use frames;
            // afterwards the same visible geometry can use one already-warmed
            // material on a combined mesh instead of paying 48 artificial
            // steady-state submissions that are outside the PSO experiment.
            if (traceRequested)
            {
                PsoTraceController trace = PsoTraceController.Instance;
                if (trace != null && trace.IsTracing)
                    return;
            }

            uniqueStatePresentationFrameCount++;
            if (uniqueStatePresentationFrameCount <= UniqueStatePresentationFrames)
                return;

            for (int index = 0; index < stateRenderers.Count; index++)
            {
                Renderer renderer = stateRenderers[index];
                if (renderer != null)
                    renderer.enabled = false;
            }
            if (steadyStateRenderer != null)
                steadyStateRenderer.enabled = true;
            stateRenderersConsolidated = true;
            markerWriter.Write(
                "STATE_BURST_CONSOLIDATED",
                Time.realtimeSinceStartupAsDouble);
        }

        private void HideCompletedRenderers()
        {
            revealLayerVisible = false;
            benchmarkCamera.cullingMask &= ~(1 << benchmarkLayer);
        }

        private void OnGUI()
        {
            if (diagnosticHudDisabled)
                return;
            if (white == null || scenario == null)
                return;

            EnsureGuiStyles();
            RefreshHudText(Time.realtimeSinceStartupAsDouble);
            DrawRect(new Rect(0, 0, Screen.width, 70), new Color(0.01f, 0.025f, 0.05f, 0.86f));
            GUI.Label(
                new Rect(22, 10, Screen.width - 44, 32),
                titleContent,
                titleStyle);
            GUI.Label(
                new Rect(23, 42, 900, 24),
                detailContent,
                labelStyle);
            GUI.Label(
                new Rect(24, 84, Screen.width - 48, 30),
                currentStatusContent,
                statusStyle);
            float footerY = Screen.height - 54;
            GUI.Label(new Rect(24, footerY, 70, 30), currentElapsedContent, labelStyle);
            GUI.Label(new Rect(92, footerY, 72, 30), footerDurationContent, labelStyle);
            GUI.Label(new Rect(164, footerY, 70, 30), footerPresentContent, labelStyle);
            GUI.Label(new Rect(234, footerY, 48, 30), currentFrameContent, labelStyle);
            GUI.Label(new Rect(282, footerY, 68, 30), footerWorstContent, labelStyle);
            GUI.Label(new Rect(350, footerY, 48, 30), currentWorstContent, labelStyle);
            GUI.Label(new Rect(398, footerY, 126, 30), footerMissedContent, labelStyle);
            GUI.Label(
                new Rect(524, footerY, 48, 30),
                currentSevereCountContent,
                labelStyle);

            if (revealLayerVisible)
            {
                Vector3 screen = benchmarkCamera.WorldToScreenPoint(
                    continuityWorldPosition);
                if (screen.z > 0.0f)
                {
                    float x = screen.x;
                    float y = Screen.height - screen.y;
                    DrawRect(new Rect(x - 34, y - 1, 22, 2), modeColor);
                    DrawRect(new Rect(x + 12, y - 1, 22, 2), modeColor);
                    DrawRect(new Rect(x - 1, y - 34, 2, 22), modeColor);
                    DrawRect(new Rect(x - 1, y + 12, 2, 22), modeColor);
                }
            }

            if (lastSevereAt >= 0.0 &&
                Time.realtimeSinceStartupAsDouble - lastSevereAt <= 0.8)
            {
                Rect alert = new Rect(Screen.width * 0.5f - 230.0f, 132.0f, 460.0f, 54.0f);
                DrawRect(alert, new Color(0.72f, 0.025f, 0.018f, 0.94f));
                GUI.Label(
                    new Rect(alert.x + 74, alert.y, 104, alert.height),
                    alertActualContent,
                    hitchStyle);
                GUI.Label(
                    new Rect(alert.x + 178, alert.y, 86, alert.height),
                    currentHitchFrameContent,
                    hitchStyle);
                GUI.Label(
                    new Rect(alert.x + 264, alert.y, 122, alert.height),
                    alertFrameContent,
                    hitchStyle);
            }
        }

        private void InitializeHudContent()
        {
            titleContent = new GUIContent(titleText);
            detailContent = new GUIContent(detailText);
            waitingStatusContent = new GUIContent("WAITING FOR CONTROLLED FLIGHT");
            revealedStatusContent = new GUIContent(
                "DISTRICT REVEALED · CONTINUITY TARGET LIVE");
            footerDurationContent = new GUIContent("/ 12.00 s");
            footerPresentContent = new GUIContent("· PRESENT");
            footerWorstContent = new GUIContent("· WORST");
            footerMissedContent = new GUIContent("· MISSED 60 FPS");
            alertActualContent = new GUIContent("ACTUAL");
            alertFrameContent = new GUIContent("ms FRAME");
            requestStatusContents = BuildStatusContents(
                "APPROACH · REQUEST IN ",
                " s",
                30);
            revealStatusContents = BuildStatusContents(
                "UPCOMING DISTRICT · ",
                " s TO DEADLINE",
                40);
            elapsedTimeContents = BuildElapsedTimeContents();
            frameTimeContents = BuildFrameTimeContents();
            severeCountContents = BuildSevereCountContents();
            currentStatusContent = waitingStatusContent;
            currentElapsedContent = elapsedTimeContents[0];
            currentFrameContent = frameTimeContents[0];
            currentHitchFrameContent = frameTimeContents[0];
            currentWorstContent = frameTimeContents[0];
            currentSevereCountContent = severeCountContents[0];
        }

        private static GUIContent[] BuildStatusContents(
            string prefix,
            string suffix,
            int maximumTenths)
        {
            var contents = new GUIContent[maximumTenths + 1];
            for (int index = 0; index < contents.Length; index++)
            {
                contents[index] = new GUIContent(
                    prefix + (index * 0.1).ToString(
                        "F1",
                        CultureInfo.InvariantCulture) + suffix);
            }
            return contents;
        }

        private static GUIContent[] BuildElapsedTimeContents()
        {
            const int maximumHundredths = 1200;
            var contents = new GUIContent[maximumHundredths + 1];
            for (int index = 0; index < contents.Length; index++)
            {
                contents[index] = new GUIContent(
                    "T+" + (index * 0.01).ToString(
                        "F2",
                        CultureInfo.InvariantCulture));
            }
            return contents;
        }

        private static GUIContent[] BuildFrameTimeContents()
        {
            const int maximumTenths = 5000;
            var contents = new GUIContent[maximumTenths + 1];
            for (int index = 0; index < maximumTenths; index++)
            {
                contents[index] = new GUIContent(
                    (index * 0.1).ToString(
                        "F1",
                        CultureInfo.InvariantCulture));
            }
            contents[maximumTenths] = new GUIContent("500.0+");
            return contents;
        }

        private static GUIContent[] BuildSevereCountContents()
        {
            const int maximumCount = 999;
            var contents = new GUIContent[maximumCount + 1];
            for (int index = 0; index < maximumCount; index++)
            {
                contents[index] = new GUIContent(
                    index.ToString(CultureInfo.InvariantCulture));
            }
            contents[maximumCount] = new GUIContent("999+");
            return contents;
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
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
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = modeColor },
            };
            hitchStyle = new GUIStyle(titleStyle)
            {
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private void RefreshHudText(double now)
        {
            if (now < nextHudTextRefreshAt && currentStatusContent != null)
                return;

            nextHudTextRefreshAt = now + 0.1;
            currentStatusContent = !scenario.IsArmed
                ? waitingStatusContent
                : !scenario.IsContentRequested
                    ? requestStatusContents[ClampLookupIndex(
                        scenario.SecondsUntilRequest(now),
                        10.0,
                        requestStatusContents.Length)]
                    : !scenario.IsContentRevealed
                        ? revealStatusContents[ClampLookupIndex(
                            scenario.SecondsUntilReveal(now),
                            10.0,
                            revealStatusContents.Length)]
                        : revealedStatusContent;
            double flightElapsed = scenario.IsArmed
                ? Math.Min(
                    RunDurationSeconds,
                    Math.Max(0.0, now - scenario.WorkloadStartedAt))
                : 0.0;
            currentElapsedContent = elapsedTimeContents[ClampLookupIndex(
                flightElapsed,
                100.0,
                elapsedTimeContents.Length)];
            currentFrameContent = frameTimeContents[ClampLookupIndex(
                currentFrameMilliseconds,
                10.0,
                frameTimeContents.Length)];
            currentWorstContent = frameTimeContents[ClampLookupIndex(
                worstFrameMilliseconds,
                10.0,
                frameTimeContents.Length)];
            currentSevereCountContent = severeCountContents[Math.Min(
                Math.Max(0, severeFrameCount),
                severeCountContents.Length - 1)];
        }

        private static int ClampLookupIndex(
            double value,
            double scale,
            int length)
        {
            return Math.Min(
                Math.Max(0, (int)Math.Round(value * scale)),
                length - 1);
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
            if (scenarioReceiptWritten ||
                scenario == null ||
                string.IsNullOrWhiteSpace(scenarioReportPath))
            {
                return;
            }
            scenarioReceiptWritten = true;

            double maximum = 0.0;
            double revealMaximum = 0.0;
            double maximumCpuTotal = 0.0;
            double maximumCpuMainThread = 0.0;
            double maximumCpuPresentWait = 0.0;
            double maximumCpuRenderThread = 0.0;
            double maximumGpu = 0.0;
            int frameTimingSampleCount = 0;
            double revealStart = requestDelaySeconds + deadlineSeconds;
            double revealEnd = revealStart + ContentCompleteDelaySeconds;
            for (int index = 0; index < frameSamples.Count; index++)
            {
                ScenarioFrameSample sample = frameSamples[index];
                maximum = Math.Max(maximum, sample.milliseconds);
                if (sample.elapsedSeconds >= revealStart && sample.elapsedSeconds <= revealEnd)
                    revealMaximum = Math.Max(revealMaximum, sample.milliseconds);
                if (sample.nativeTimingAvailable)
                {
                    frameTimingSampleCount++;
                    maximumCpuTotal = Math.Max(
                        maximumCpuTotal,
                        sample.cpuTotalMilliseconds);
                    maximumCpuMainThread = Math.Max(
                        maximumCpuMainThread,
                        sample.cpuMainThreadMilliseconds);
                    maximumCpuPresentWait = Math.Max(
                        maximumCpuPresentWait,
                        sample.cpuMainThreadPresentWaitMilliseconds);
                    maximumCpuRenderThread = Math.Max(
                        maximumCpuRenderThread,
                        sample.cpuRenderThreadMilliseconds);
                    maximumGpu = Math.Max(
                        maximumGpu,
                        sample.gpuMilliseconds);
                }
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
                phase = phase,
                generatedUtc = PsoFileUtility.UtcNowText(),
                completed = completed && scenario.IsContentCompleted,
                runDurationSeconds = RunDurationSeconds,
                contentRequestSeconds = requestDelaySeconds,
                contentRevealSeconds = revealStart,
                revealWindowEndSeconds = revealEnd,
                maximumMilliseconds = maximum,
                revealWindowMaximumMilliseconds = revealMaximum,
                firstPostRevealFrameMilliseconds = firstPostRevealFrameMilliseconds,
                severeFrameThresholdMilliseconds = SevereFrameMilliseconds,
                severeFrameCount = severeFrameCount,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                qualityLevelName = PsoUnityEnvironment.CurrentQualityName(),
                outputWidth = Screen.width,
                outputHeight = Screen.height,
                internalRenderScale = appliedRenderScale,
                internalRenderScaleApplied = renderScaleApplied,
                cameraFarClipPlane = appliedFarClipPlane,
                controlledGraphicsStateCount = DeferredGpuStateCount,
                deterministicCameraPass = true,
                simulationTimeScale = ControlledSimulationTimeScale,
                cameraMotion =
                    "12-second closed camera-local city circuit; fixed T+0 phase; unscaled realtime",
                cameraMotionPreconditioned = true,
                cameraCircuitSeconds = CameraCircuitSeconds,
                cameraCircuitCompletedBeforeMeasurement =
                    cameraCircuitCompletedBeforeMeasurement,
                cameraCircuitPhaseAtArmSeconds = cameraCircuitPhaseAtArm,
                cameraOriginWorldX = controlledCameraStartPosition.x,
                cameraOriginWorldY = controlledCameraStartPosition.y,
                cameraOriginWorldZ = controlledCameraStartPosition.z,
                cameraOriginRotationX = controlledCameraStartRotation.x,
                cameraOriginRotationY = controlledCameraStartRotation.y,
                cameraOriginRotationZ = controlledCameraStartRotation.z,
                cameraOriginRotationW = controlledCameraStartRotation.w,
                captureConditioned = captureReadyAnnounced &&
                    scenario.WorkloadStartedAt > captureReadyAt,
                captureConditioningLeadSeconds = Math.Max(
                    0.0,
                    scenario.WorkloadStartedAt - captureReadyAt),
                startupQuiescenceRequired = true,
                startupQuiescenceRequiredSeconds =
                    StartupQuiescenceRequiredSeconds,
                startupQuiescenceFrameCeilingMilliseconds =
                    StartupQuiescenceFrameCeilingMilliseconds,
                startupQuiescenceObservedSeconds =
                    startupQuiescenceObservedSeconds,
                startupQuiescenceResetCount = startupQuiescenceResetCount,
                controlledPreflightSeconds = Math.Max(
                    0.0,
                    scenario.WorkloadStartedAt - controlledFlightStartedAt),
                preflightFrameSampleCount = preflightFrameSamples.Count,
                preflightMeanMilliseconds = PreflightMeanMilliseconds(),
                preflightP95Milliseconds = Percentile(
                    preflightFrameSamples,
                    0.95),
                preflightMaximumMilliseconds = preflightMaximumMilliseconds,
                preflightNativeTimingSampleCount =
                    preflightNativeTimingSampleCount,
                preflightMaximumCpuTotalMilliseconds =
                    preflightMaximumCpuTotalMilliseconds,
                preflightMeanCpuTotalMilliseconds = MeanNativeTiming(
                    preflightCpuTotalMilliseconds),
                preflightMaximumCpuMainThreadMilliseconds =
                    preflightMaximumCpuMainThreadMilliseconds,
                preflightMeanCpuMainThreadMilliseconds = MeanNativeTiming(
                    preflightCpuMainThreadMilliseconds),
                preflightMaximumCpuMainThreadPresentWaitMilliseconds =
                    preflightMaximumCpuMainThreadPresentWaitMilliseconds,
                preflightMeanCpuMainThreadPresentWaitMilliseconds =
                    MeanNativeTiming(
                        preflightCpuMainThreadPresentWaitMilliseconds),
                preflightMaximumCpuRenderThreadMilliseconds =
                    preflightMaximumCpuRenderThreadMilliseconds,
                preflightMeanCpuRenderThreadMilliseconds = MeanNativeTiming(
                    preflightCpuRenderThreadMilliseconds),
                preflightMaximumGpuMilliseconds = preflightMaximumGpuMilliseconds,
                preflightMeanGpuMilliseconds = MeanNativeTiming(
                    preflightGpuMilliseconds),
                preflightManagedGcCollectionCountAtStart =
                    preflightManagedGcCollectionCountAtStart,
                preflightManagedGcCollectionCountAtEnd = GC.CollectionCount(0),
                preflightManagedGcCollections = Math.Max(
                    0,
                    GC.CollectionCount(0) -
                    preflightManagedGcCollectionCountAtStart),
                preflightFramesAtOrAboveQuiescenceCeiling =
                    preflightFramesAtOrAboveQuiescenceCeiling,
                preflightFramesAtOrAbovePresentationBudget =
                    preflightFramesAtOrAbovePresentationBudget,
                diagnosticHudDisabled = diagnosticHudDisabled,
                diagnosticStaticCamera = diagnosticStaticCamera,
                diagnosticFarClipPlane = diagnosticFarClipPlane,
                diagnosticTargetFrameRate = diagnosticTargetFrameRate,
                targetFrameRate = effectiveTargetFrameRate,
                vSyncCount = QualitySettings.vSyncCount,
                frameTimingStatsEnabled = frameTimingStatsEnabled,
                frameTimingSampleCount = frameTimingSampleCount,
                maximumCpuTotalMilliseconds = maximumCpuTotal,
                maximumCpuMainThreadMilliseconds = maximumCpuMainThread,
                maximumCpuMainThreadPresentWaitMilliseconds = maximumCpuPresentWait,
                maximumCpuRenderThreadMilliseconds = maximumCpuRenderThread,
                maximumGpuMilliseconds = maximumGpu,
                managedGcCollectionCountAtStart =
                    managedGcCollectionCountAtStart,
                managedGcCollectionCountAtEnd =
                    managedGcCollectionCountAtEnd >= 0
                        ? managedGcCollectionCountAtEnd
                        : GC.CollectionCount(0),
                managedGcCollectionsDuringRun = Math.Max(
                    0,
                    (managedGcCollectionCountAtEnd >= 0
                        ? managedGcCollectionCountAtEnd
                        : GC.CollectionCount(0)) -
                    managedGcCollectionCountAtStart),
                deferredReady = scenario.IsDeferredReady,
                deadlineMissed = scenario.DeadlineMissed,
                deferredReadySeconds = scenario.IsDeferredReady
                    ? Math.Max(
                        0.0,
                        scenario.DeferredReadyAt - scenario.WorkloadStartedAt)
                    : -1.0,
                simulationSystemsFrozen = frozenSimulationWorldNames.Count > 0,
                frozenSimulationWorldCount = frozenSimulationWorldNames.Count,
                frozenSimulationWorlds = frozenSimulationWorldNames.ToArray(),
                initializationSystemsFrozen =
                    frozenInitializationWorldNames.Count > 0,
                frozenInitializationWorldCount =
                    frozenInitializationWorldNames.Count,
                frozenInitializationWorlds =
                    frozenInitializationWorldNames.ToArray(),
                presentationSystemsRemainActive =
                    activePresentationWorldNames.Count > 0,
                activePresentationWorldCount =
                    activePresentationWorldNames.Count,
                activePresentationWorlds =
                    activePresentationWorldNames.ToArray(),
                frameSamples = frameSamples.ToArray(),
            };
            PsoFileUtility.WriteJsonAtomic(scenarioReportPath, receipt);
        }

        private void OnDestroy()
        {
            WriteScenarioReceipt(runComplete);
            markerWriter?.Flush();
            if (controlledFlightStarted)
            {
                Time.timeScale = originalTimeScale;
                RestoreFrozenSystemGroups();
            }
            Application.targetFrameRate = originalTargetFrameRate;
            QualitySettings.vSyncCount = originalVSyncCount;
            if (renderScaleApplied &&
                benchmarkRenderPipelineAsset != null &&
                benchmarkRenderScaleProperty != null)
            {
                benchmarkRenderScaleProperty.SetValue(
                    benchmarkRenderPipelineAsset,
                    originalRenderScale);
            }
            if (benchmarkCamera != null)
            {
                benchmarkCamera.cullingMask = originalCameraCullingMask;
                benchmarkCamera.farClipPlane = originalFarClipPlane;
            }
            if (white != null)
                Destroy(white);
            if (steadyStateMesh != null)
                Destroy(steadyStateMesh);
            for (int index = 0; index < stateMaterials.Count; index++)
            {
                if (stateMaterials[index] != null)
                    Destroy(stateMaterials[index]);
            }
            stateMaterials.Clear();
            stateRenderers.Clear();
        }
    }
}
