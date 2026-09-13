using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Unity.MegacityMetro.CameraManagement;
using Unity.MegacityMetro.Gameplay;
using Unity.MegacityMetro.UI;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Yanagisawa.ShaderHitchPipeline;

// Explicit host adaptation, not an upstream CLI claim. The opt-in entry calls
// the same ordinary application API as the original UI after initialized Menu
// has really rendered. It does not invoke callbacks, simulate input, change the
// route/camera, hide LoadingScreen, or freeze any original simulation.
[DefaultExecutionOrder(-31900)]
public sealed class PsoMegacityAcceptanceCapture : MonoBehaviour
{
    [Serializable] public struct Frame
    {
        public int frame;
        public string scene, status;
        public double seconds, updateIntervalMilliseconds;
        public long allocatedBytes, reservedBytes;
        public bool loadingVisible, tutorialVisible, focused;
    }
    [Serializable] public struct Render
    {
        public int frame, cameraId, pixelWidth, pixelHeight;
        public string scene, camera, cameraType;
        public double seconds;
        public Vector3 position;
        public Quaternion rotation;
        public bool targetTexture, originalHybridCamera, hybridInitialized, loadingVisible, tutorialVisible;
    }
    [Serializable] public struct Event
    {
        public string kind, scene, detail;
        public int frame;
        public double seconds, engineRealtimeSeconds;
    }
    [Serializable] public sealed class Capture
    {
        public int schemaVersion = 1;
        public string startedUtc, finishedUtc, unityVersion, buildGuid, graphicsApi, gpu, quality;
        public string route = "Opt-in shared application API: initialized rendered Menu -> original SinglePlayer mode -> original async Main; original stationary player camera and evolving content; no injected trajectory.";
        public string timingScope = "CPU Update intervals and endCameraRendering submissions, not GPU completion or presentation; first load included; pre-first-Update and post-last-Update gaps reported separately.";
        public bool applicationQuit, screenshotsEnabled, singlePlayerEntryAccepted, contentReadinessObserved, originalQuitRequested;
        public int width, height, errors, exceptions, vSyncCount, targetFrameRate;
        public double elapsedSeconds, observerRealtimeOriginSeconds, requestedObservationSeconds;
        public PsoEnvironmentSnapshot environment;
        public Frame[] frames;
        public Render[] renders;
        public Event[] events;
        public PsoMegacityContentObserver.Snapshot[] worldSnapshots;
    }
    public static PsoMegacityAcceptanceCapture Instance { get; private set; }
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<Frame> frames = new List<Frame>(32768);
    readonly List<Render> renders = new List<Render>(32768);
    readonly List<Event> events = new List<Event>();
    readonly List<PsoMegacityContentObserver.Snapshot> snapshots = new List<PsoMegacityContentObserver.Snapshot>();
    readonly HashSet<int> screenshotStages = new HashSet<int>();
    readonly Dictionary<ulong, PsoMegacityContentObserver.Snapshot> latestWorlds = new Dictionary<ulong, PsoMegacityContentObserver.Snapshot>();
    Capture capture;
    PsoExternalPhaseBridge bridge;
    string output, previousStatus;
    double previousUpdate, readyAt = -1, lastProgress;
    int menuRenderedFrame = -1, nativeMainRenderFrame = -1;
    bool finished, tutorialDismissed, quitSent, phaseDemandObserved;
    const string Menu = "Assets/Scenes/Menu.unity", Main = "Assets/Scenes/Main.unity";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void Bootstrap()
    {
        if (!PsoCommandLine.Current.HasFlag("-pso-external-capture")) return;
        var host = new GameObject("PSO Megacity application acceptance observer");
        DontDestroyOnLoad(host); host.AddComponent<PsoMegacityAcceptanceCapture>();
    }
    void Awake()
    {
        Instance = this;
        if (Application.isBatchMode || !PsoCommandLine.Current.HasFlag("-pso-megacity-single-player"))
            throw new InvalidOperationException("This cell requires the explicit shared-API entry and a normal non-batch Player.");
        output = PsoCommandLine.Current.GetString(PsoConstants.OutputArgument, "");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Explicit evidence output required.");
        Directory.CreateDirectory(output);
        if (File.Exists(Path.Combine(output, "external-capture.json"))) throw new IOException("Retain existing capture.");
        capture = new Capture { startedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
            buildGuid = Application.buildGUID, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName,
            screenshotsEnabled = PsoCommandLine.Current.HasFlag("-pso-external-screenshots"),
            requestedObservationSeconds = PsoCommandLine.Current.GetDouble("-pso-megacity-observe-seconds", 60, 30, 600) };
        capture.observerRealtimeOriginSeconds = Time.realtimeSinceStartupAsDouble - clock.Elapsed.TotalSeconds;
        SceneManager.sceneLoaded += Loaded; SceneManager.sceneUnloaded += Unloaded;
        RenderPipelineManager.endCameraRendering += Rendered;
        Application.logMessageReceivedThreaded += Message;
        bridge = PsoExternalPhaseBridge.AttachManual(gameObject, "megacity-process");
        Record("observer-before-splash", "Process-wide observation; SubScenes are not isolated trace phases.");
    }
    bool LoadingVisible => LoadingScreen.Instance != null && LoadingScreen.Instance.IsVisible;
    bool TutorialVisible => TutorialScreen.Instance != null && TutorialScreen.Instance.IsShowingTutorial;
    Camera OriginalCamera => HybridCameraManager.Instance == null || HybridCameraManager.Instance.CameraObject == null
        ? null : HybridCameraManager.Instance.CameraObject.GetComponent<Camera>();

    public void Observe(PsoMegacityContentObserver.Snapshot value)
    {
        snapshots.Add(value); latestWorlds[value.worldSequence] = value;
        if (!phaseDemandObserved && value.allExpectedScenesLoaded && SceneManager.GetActiveScene().path == Main)
        {
            phaseDemandObserved = true;
            bridge.ObserveOriginalWarmupWindow(LoadingVisible, 1000);
            Record("aggregate-process-phase-dependencies-observed", "All six actual requested/loaded payloads in world=" + value.world +
                "; one process-wide collection, not six isolated traces. Original loading screen visible=" + LoadingVisible);
            bridge.ObserveDependenciesReady("megacity-main-native-six-subscenes", "megacity-process");
        }
    }
    void Loaded(Scene scene, LoadSceneMode mode) => Record("scene-loaded-not-first-draw", scene.path + ":" + mode);
    void Unloaded(Scene scene) => Record("scene-unloaded", scene.path);
    void Record(string kind, string detail)
    {
        var value = new Event { kind = kind, detail = detail, scene = SceneManager.GetActiveScene().path,
            frame = Time.frameCount, seconds = clock.Elapsed.TotalSeconds,
            engineRealtimeSeconds = Time.realtimeSinceStartupAsDouble };
        events.Add(value); UnityEngine.Debug.Log("[PSO Megacity Observer] " + JsonUtility.ToJson(value));
    }

    void Update()
    {
        double now = clock.Elapsed.TotalSeconds;
        string scene = SceneManager.GetActiveScene().path;
        string status = scene == Menu ? "Menu" : scene == Main ? (LoadingVisible ? "MainLoading" : "MainVisible") : "Other";
        frames.Add(new Frame { frame = Time.frameCount, seconds = now,
            updateIntervalMilliseconds = frames.Count == 0 ? 0 : (now - previousUpdate) * 1000,
            scene = scene, status = status, loadingVisible = LoadingVisible, tutorialVisible = TutorialVisible,
            focused = Application.isFocused, allocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), reservedBytes = Profiler.GetTotalReservedMemoryLong() });
        previousUpdate = now;
        bridge.ObserveOriginalWarmupWindow(LoadingVisible, 1000);
        if (status != previousStatus) { previousStatus = status; Record("original-route-state", status); }
        if (!capture.singlePlayerEntryAccepted && scene == Menu && menuRenderedFrame >= 0 && Time.frameCount > menuRenderedFrame)
        {
            var menu = MainMenu.Instance;
            if (menu != null && menu.IsInitialized && menu.TryBeginSinglePlayer())
            {
                capture.singlePlayerEntryAccepted = true;
                Record("shared-api-single-player-entry", "Original mode assignment, SceneController.LoadGame and Hide completed; original async loader still owns Main.");
            }
        }
        if (readyAt < 0 && scene == Main && !LoadingVisible && nativeMainRenderFrame >= 0 &&
            HybridCameraManager.Instance != null && HybridCameraManager.Instance.WasInitialized &&
            PlayerInfoController.Instance != null && PlayerInfoController.Instance.IsSinglePlayer)
        {
            foreach (var pair in latestWorlds)
            {
                var value = pair.Value;
                if (Time.realtimeSinceStartupAsDouble - value.realtimeSeconds > 2 || !value.allExpectedScenesLoaded ||
                    !value.gameLoadInfoPresent || value.requestedSections <= 0 || value.loadedSections != value.requestedSections ||
                    value.singlePlayers <= 0 || value.renderEntities <= 0 || value.vehicles <= 0 || value.blimps <= 0) continue;
                readyAt = now; capture.contentReadinessObserved = true;
                Record("six-payloads-original-camera-single-player-ready", "world=" + value.world + "; request/load/payload counts are distinct from first draw; dynamic movement is validated offline.");
                break;
            }
        }
        // Explicit application action shared with the real input subscription;
        // only after the original loading system independently hid its screen.
        if (!tutorialDismissed && readyAt >= 0 && TutorialVisible)
        {
            TutorialScreen.Instance.DismissTutorial(); tutorialDismissed = true;
            Record("shared-api-tutorial-dismissed", "Instruction overlay only; loading screen was already hidden by the original game system.");
        }
        if (!quitSent && readyAt >= 0 && now - readyAt >= capture.requestedObservationSeconds)
        {
            quitSent = true; capture.originalQuitRequested = true;
            Record("original-quit-system-requested", "Fixed declared observation duration elapsed; movement/render correctness remains an offline gate.");
            QuitSystem.WantsToQuit = true;
        }
        if (now - lastProgress >= 10)
        {
            lastProgress = now;
            Record("progress", "status=" + status + "; ready=" + capture.contentReadinessObserved + "; observationSeconds=" + (readyAt < 0 ? 0 : now - readyAt));
        }
        if (readyAt < 0 && now > 480 && !quitSent)
        {
            quitSent = true; Record("acceptance-timeout", "Readiness failed; normal failure exit, no workload substitution."); Application.Quit(79);
        }
        if (quitSent && readyAt >= 0 && now - readyAt > capture.requestedObservationSeconds + 30)
        {
            Record("original-quit-system-timeout", "Native quit request did not exit; failure fallback."); Application.Quit(80);
        }
    }

    void Rendered(ScriptableRenderContext context, Camera camera)
    {
        bool native = camera == OriginalCamera;
        string scene = camera.gameObject.scene.path;
        if (scene == Menu && !camera.targetTexture && camera.cameraType == CameraType.Game) menuRenderedFrame = Time.frameCount;
        if (native && scene == Main && !camera.targetTexture) nativeMainRenderFrame = Time.frameCount;
        renders.Add(new Render { frame = Time.frameCount, seconds = clock.Elapsed.TotalSeconds, scene = scene,
            cameraId = camera.GetInstanceID(), camera = camera.name, cameraType = camera.cameraType.ToString(),
            targetTexture = camera.targetTexture != null, pixelWidth = camera.pixelWidth, pixelHeight = camera.pixelHeight,
            position = camera.transform.position, rotation = camera.transform.rotation, originalHybridCamera = native,
            hybridInitialized = HybridCameraManager.Instance != null && HybridCameraManager.Instance.WasInitialized,
            loadingVisible = LoadingVisible, tutorialVisible = TutorialVisible });
        if (!capture.screenshotsEnabled || !native || LoadingVisible || TutorialVisible || readyAt < 0) return;
        double elapsed = clock.Elapsed.TotalSeconds - readyAt;
        int stage = elapsed >= capture.requestedObservationSeconds * 0.8 ? 2 : elapsed >= capture.requestedObservationSeconds * 0.5 ? 1 : 0;
        if (screenshotStages.Add(stage))
        {
            string file = "main-original-camera-" + stage + ".png";
            ScreenCapture.CaptureScreenshot(Path.Combine(output, file));
            Record("rendered-screenshot-requested", file);
        }
    }
    void Message(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception) Interlocked.Increment(ref capture.exceptions);
        if (type == LogType.Error || type == LogType.Assert) Interlocked.Increment(ref capture.errors);
    }
    void OnApplicationQuit()
    {
        if (finished || capture == null) return;
        finished = true; Record("application-quitting", "Callback time precedes evidence serialization and engine shutdown.");
        capture.applicationQuit = true; capture.elapsedSeconds = clock.Elapsed.TotalSeconds;
        capture.finishedUtc = DateTime.UtcNow.ToString("o");
        capture.width = Screen.width; capture.height = Screen.height; capture.quality = PsoUnityEnvironment.CurrentQualityName();
        capture.vSyncCount = QualitySettings.vSyncCount; capture.targetFrameRate = Application.targetFrameRate;
        capture.environment = PsoUnityEnvironment.Capture();
        capture.frames = frames.ToArray(); capture.renders = renders.ToArray(); capture.events = events.ToArray();
        capture.worldSnapshots = snapshots.ToArray();
        File.WriteAllText(Path.Combine(output, "external-capture.json"), JsonUtility.ToJson(capture));
        UnityEngine.Debug.Log("[PSO Megacity Observer] capture-written; frames=" + frames.Count + "; renderSubmissions=" + renders.Count);
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= Loaded; SceneManager.sceneUnloaded -= Unloaded;
        RenderPipelineManager.endCameraRendering -= Rendered; Application.logMessageReceivedThreaded -= Message;
        if (Instance == this) Instance = null;
    }
}
