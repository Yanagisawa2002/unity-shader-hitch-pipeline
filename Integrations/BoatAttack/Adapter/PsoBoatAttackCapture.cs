using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BoatAttack.Benchmark;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Yanagisawa.ShaderHitchPipeline;

// An observer of the original benchmark and render callbacks. Never loads a
// scene, changes a camera, advances a route, generates content or sends input.
[DefaultExecutionOrder(-31900)]
public sealed class PsoBoatAttackCapture : MonoBehaviour
{
    [Serializable] public struct Frame
    {
        public int frame, run, routeFrame;
        public string scene, route;
        public double seconds, deltaMilliseconds, updateIntervalMilliseconds;
        public long allocatedBytes, reservedBytes;
    }
    [Serializable] public struct Render
    {
        public int frame, run, routeFrame, cameraId;
        public string scene, route, camera, cameraType;
        public double seconds;
        public Vector3 position;
        public Quaternion rotation;
        public int pixelWidth, pixelHeight;
        public bool targetTexture;
    }
    [Serializable] public struct Event
    {
        public string kind, scene;
        public int frame, rootObjects;
        public double seconds;
    }
    [Serializable] public sealed class Capture
    {
        public int schemaVersion = 1;
        public string startedUtc, finishedUtc, unityVersion, buildGuid, graphicsApi, gpu, quality;
        public bool applicationQuit, screenshotsEnabled;
        public int width, height, errors, exceptions;
        public double elapsedSeconds, observerRealtimeOriginSeconds;
        public Frame[] frames;
        public Render[] renders;
        public Event[] events;
        public string timingScope = "Update intervals and endCameraRendering submissions, not GPU completion or presentation timestamps. Includes upstream warmup; readiness is not first visibility.";
    }
    readonly List<Frame> frames = new List<Frame>(32768);
    readonly List<Render> renders = new List<Render>(65536);
    readonly List<Event> events = new List<Event>(128);
    readonly HashSet<string> screenshots = new HashSet<string>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    Capture capture;
    string output, sceneName = "", routeName = "";
    double previousUpdate;
    bool finished;
    double nextDiagnostic = 10;
    bool diagnostic;
    PsoExternalPhaseBridge bridge;
    double warmupWindowMilliseconds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void Bootstrap()
    {
        if (!PsoCommandLine.Current.HasFlag("-pso-external-capture")) return;
        var host = new GameObject("PSO external Boat Attack observer");
        DontDestroyOnLoad(host);
        host.AddComponent<PsoBoatAttackCapture>();
    }
    void Awake()
    {
        output = PsoCommandLine.Current.GetString(PsoConstants.OutputArgument, "");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("External capture requires an explicit output directory.");
        Directory.CreateDirectory(output);
        if (File.Exists(Path.Combine(output, "external-capture.json"))) throw new IOException("Retain existing capture; use a new directory.");
        capture = new Capture { startedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
            buildGuid = Application.buildGUID, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName,
            screenshotsEnabled = PsoCommandLine.Current.HasFlag("-pso-external-screenshots") };
        capture.observerRealtimeOriginSeconds = Time.realtimeSinceStartupAsDouble - clock.Elapsed.TotalSeconds;
        warmupWindowMilliseconds = PsoCommandLine.Current.GetDouble("-pso-external-warmup-window-ms", 1000, 1, 60000);
        diagnostic = PsoCommandLine.Current.HasFlag("-pso-external-diagnostic");
        SceneManager.sceneLoaded += Loaded;
        SceneManager.sceneUnloaded += Unloaded;
        RenderPipelineManager.endCameraRendering += Rendered;
        Application.logMessageReceivedThreaded += Message;
        PsoExternalPhaseBridge.Attach(gameObject, new[] {
            "Assets/scenes/Testing/benchmark_island-flythrough.unity",
            "Assets/scenes/Testing/benchmark_island-static.unity"
        }, new[] { "boat-flythrough", "boat-static" }, "boat-loading");
        bridge = GetComponent<PsoExternalPhaseBridge>();
        AddEvent("observer-before-splash", default);
    }
    void Loaded(Scene scene, LoadSceneMode mode) { AddEvent("scene-loaded-not-first-draw", scene); }
    void Unloaded(Scene scene) { AddEvent("scene-unloaded", scene); }
    void AddEvent(string kind, Scene scene)
    {
        events.Add(new Event { kind = kind, scene = scene.IsValid() ? scene.path : "", frame = Time.frameCount,
            seconds = clock.Elapsed.TotalSeconds, rootObjects = scene.IsValid() && scene.isLoaded ? scene.rootCount : 0 });
        UnityEngine.Debug.Log("[PSO Boat Observer] " + JsonUtility.ToJson(events[events.Count - 1]));
    }
    void Update()
    {
        double now = clock.Elapsed.TotalSeconds;
        sceneName = SceneManager.GetActiveScene().path;
        routeName = Benchmark.Current == null ? "" : Benchmark.Current.benchmarkName;
        // These are the upstream benchmark's existing noninteractive warmup
        // traversals. Keep rendering and all samples; no extra wait or load gate.
        bridge.ObserveOriginalWarmupWindow(Benchmark.Current != null && Benchmark.CurrentRunIndex == -1 &&
            Benchmark.Current.scene == sceneName, warmupWindowMilliseconds);
        frames.Add(new Frame { frame = Time.frameCount, seconds = now, scene = sceneName, route = routeName,
            run = Benchmark.CurrentRunIndex, routeFrame = Benchmark.CurrentRunFrame,
            deltaMilliseconds = Time.unscaledDeltaTime * 1000.0,
            updateIntervalMilliseconds = previousUpdate == 0 ? 0 : (now - previousUpdate) * 1000.0,
            allocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), reservedBytes = Profiler.GetTotalReservedMemoryLong() });
        previousUpdate = now;
        if (frames.Count == 1) AddEvent("first-update", SceneManager.GetActiveScene());
        if (diagnostic && now >= nextDiagnostic)
        {
            nextDiagnostic += 10;
            UnityEngine.Debug.Log("[PSO Boat Observer] diagnostic frames=" + frames.Count + "; renders=" + renders.Count +
                "; scene=" + sceneName + "; expected=" + (Benchmark.Current == null ? "" : Benchmark.Current.scene) +
                "; run=" + Benchmark.CurrentRunIndex + "; routeFrame=" + Benchmark.CurrentRunFrame + "; focus=" + Application.isFocused);
            // Failure-only diagnostic watchdog; the native benchmark remains the
            // normal exit authority. Formal runs do not set this diagnostic flag.
            if (now >= 90)
            {
                AddEvent("diagnostic-timeout-not-benchmark-success", SceneManager.GetActiveScene());
                OnApplicationQuit();
                Application.Quit(2);
            }
        }
    }
    void Rendered(ScriptableRenderContext context, Camera camera)
    {
        if (renders.Count == 0) AddEvent("first-end-camera-rendering-not-presentation", SceneManager.GetActiveScene());
        renders.Add(new Render { frame = Time.frameCount, seconds = clock.Elapsed.TotalSeconds,
            scene = camera.gameObject.scene.path, route = routeName, run = Benchmark.CurrentRunIndex,
            routeFrame = Benchmark.CurrentRunFrame, cameraId = camera.GetInstanceID(), camera = camera.name,
            cameraType = camera.cameraType.ToString(), position = camera.transform.position, rotation = camera.transform.rotation,
            pixelWidth = camera.pixelWidth, pixelHeight = camera.pixelHeight, targetTexture = camera.targetTexture != null });
        if (!capture.screenshotsEnabled || camera.cameraType != CameraType.Game || camera.targetTexture != null ||
            Benchmark.Current == null) return;
        int routeFrame = Benchmark.CurrentRunFrame;
        if (!((Benchmark.CurrentRunIndex == -1 && (routeFrame == 5 || routeFrame == 250)) ||
              (Benchmark.CurrentRunIndex == 2 && routeFrame == 5))) return;
        string key = PsoFileUtility.SanitizeFileName(routeName) + "-run-" + Benchmark.CurrentRunIndex + "-frame-" + routeFrame;
        if (screenshots.Add(key))
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(output, key + ".png"));
            AddEvent("screenshot-request-" + key, SceneManager.GetActiveScene());
        }
    }
    void Message(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Assert) Interlocked.Increment(ref capture.errors);
        if (type == LogType.Exception) Interlocked.Increment(ref capture.exceptions);
    }
    void OnApplicationQuit()
    {
        if (finished || capture == null) return;
        finished = true;
        AddEvent("application-quit", SceneManager.GetActiveScene());
        capture.applicationQuit = true;
        capture.finishedUtc = DateTime.UtcNow.ToString("o");
        capture.elapsedSeconds = clock.Elapsed.TotalSeconds;
        capture.quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
        capture.width = Screen.width; capture.height = Screen.height;
        capture.frames = frames.ToArray(); capture.renders = renders.ToArray(); capture.events = events.ToArray();
        File.WriteAllText(Path.Combine(output, "external-capture.json"), JsonUtility.ToJson(capture));
        // The native writer uses minute-resolution filenames in persistentDataPath.
        // Preserve this process's completed native files before a later run can
        // reuse that name. Never remove or rewrite the native result directory.
        string nativeResults = Path.Combine(Application.persistentDataPath, "PerformanceResults");
        if (Directory.Exists(nativeResults))
        {
            string retained = Path.Combine(output, "upstream-results");
            Directory.CreateDirectory(retained);
            foreach (string path in Directory.GetFiles(nativeResults, "*.json"))
                if (File.GetLastWriteTimeUtc(path) >= DateTime.Parse(capture.startedUtc).ToUniversalTime().AddSeconds(-2))
                    File.Copy(path, Path.Combine(retained, Path.GetFileName(path)), false);
        }
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneUnloaded -= Unloaded;
        RenderPipelineManager.endCameraRendering -= Rendered;
        Application.logMessageReceivedThreaded -= Message;
    }
}
