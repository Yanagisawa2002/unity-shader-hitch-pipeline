using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yanagisawa.ShaderHitchPipeline;

// Host adapter: observes original loading, uses the actual Core lifecycle and
// Unity orchestrator. No scene activation gate, camera changes or asset loading.
public sealed class PsoExternalPhaseBridge : MonoBehaviour
{
    [Serializable] sealed class Evidence
    {
        public string kind, phase, scene, status, detail;
        public int frame, retainedShaderCount;
        public double seconds;
        public long generation;
    }
    sealed class Sink : IPsoContentPhaseSink
    {
        readonly PsoExternalPhaseBridge owner;
        public Sink(PsoExternalPhaseBridge owner) { this.owner = owner; }
        PsoWarmupOrchestrator Pipeline => PsoWarmupOrchestrator.Instance;
        public PsoContentPhaseActivation Activate(string phase)
        {
            var pipeline = Pipeline;
            if (pipeline == null || !pipeline.HasLoadedPlan || pipeline.HasFailed) return PsoContentPhaseActivation.Unavailable;
            try
            {
                if (pipeline.ActivatePhase(phase)) return PsoContentPhaseActivation.Accepted;
            }
            catch (PsoCollectionNotReadyException error)
            {
                owner.resolutionAttempts.TryGetValue(phase, out int attempts);
                owner.resolutionAttempts[phase] = ++attempts;
                if (attempts >= 16)
                    throw new InvalidOperationException("Dependency resolution exhausted 16 actual activation attempts: " + error.Message, error);
                owner.Record("collection-unresolved-retry", phase, error.Message);
                return PsoContentPhaseActivation.Deferred;
            }
            if (pipeline.IsPhaseUnloading(phase)) return PsoContentPhaseActivation.Deferred;
            var status = pipeline.GetPhaseStatus(phase);
            return status != null && (status.State == PsoPhaseState.Pending || status.State == PsoPhaseState.Running || status.IsComplete)
                ? PsoContentPhaseActivation.Accepted : PsoContentPhaseActivation.Unavailable;
        }
        public void Cancel(string phase) { if (Pipeline != null) Pipeline.CancelPhase(phase); }
        public void Unload(string phase) { if (Pipeline != null) Pipeline.UnloadPhase(phase); }
        public bool IsComplete(string phase)
        {
            if (Pipeline != null && Pipeline.HasFailed) throw new InvalidOperationException(Pipeline.Failure);
            return Pipeline != null && Pipeline.IsPhaseComplete(phase);
        }
    }
    readonly Dictionary<string, string> scenes = new Dictionary<string, string>(StringComparer.Ordinal);
    readonly List<PsoContentPhaseRequest> requests = new List<PsoContentPhaseRequest>();
    readonly Dictionary<long, PsoContentPhaseStatus> lastStatus = new Dictionary<long, PsoContentPhaseStatus>();
    readonly Dictionary<string, int> resolutionAttempts = new Dictionary<string, int>(StringComparer.Ordinal);
    // The common host retains the shaders it actually encounters in every arm,
    // including disabled. This deliberately declared resource-retention protocol
    // protects pending native jobs across the original UnloadUnusedAssets calls.
    // It does not load a shader whitelist or preload new content.
    readonly HashSet<Shader> retainedShaders = new HashSet<Shader>();
    PsoContentPhaseLifecycle lifecycle;
    string loadingPhase, revision;
    bool renderCold, trainingPhases, requestedLoading, quitting;
    bool windowActive, reportedFeedbackArmed;
    bool manualDependencies;

    public static void Attach(GameObject host, string[] scenePaths, string[] phases, string loading)
    {
        if (scenePaths.Length != phases.Length) throw new ArgumentException("Scene/phase mapping length differs.");
        var bridge = host.AddComponent<PsoExternalPhaseBridge>();
        for (int i = 0; i < scenePaths.Length; ++i) bridge.scenes.Add(scenePaths[i], phases[i]);
        bridge.loadingPhase = loading;
        bridge.renderCold = PsoCommandLine.Current.HasFlag(PsoConstants.DisableWarmupArgument) ||
                            PsoCommandLine.Current.HasFlag("-pso-external-render-cold");
        bridge.trainingPhases = PsoCommandLine.Current.HasFlag("-pso-external-train-phases");
        bridge.lifecycle = new PsoContentPhaseLifecycle(new Sink(bridge));
        SceneManager.sceneLoaded += bridge.Loaded;
        SceneManager.sceneUnloaded += bridge.Unloaded;
    }
    // Explicit application/ECS readiness can differ from SceneManager.loaded.
    // Reuse the same real lifecycle/sink without assigning a process-wide GSC
    // to individual SubScenes or treating a scene label as resource readiness.
    public static PsoExternalPhaseBridge AttachManual(GameObject host, string processPhase)
    {
        Attach(host, Array.Empty<string>(), Array.Empty<string>(), processPhase);
        var bridge = host.GetComponent<PsoExternalPhaseBridge>();
        bridge.manualDependencies = true;
        return bridge;
    }
    public void ObserveDependenciesReady(string content, string phase)
    {
        if (!manualDependencies) throw new InvalidOperationException("Explicit readiness requires manual attachment.");
        foreach (var request in requests) if (request.ContentId == content) return;
        RetainLoadedShaders();
        if (string.IsNullOrEmpty(revision)) revision = PsoUnityBuildIdentity.Capture().contentRevision;
        Request(content, phase);
        Dispatch();
    }
    public void ObserveOriginalWarmupWindow(bool active, double budgetMilliseconds)
    {
        var pipeline = PsoWarmupOrchestrator.Instance;
        if (pipeline != null && pipeline.HasLoadedPlan) pipeline.SetNonInteractiveWindow(active, budgetMilliseconds);
        if (active != windowActive)
        {
            windowActive = active;
            Record(active ? "original-warmup-window-open" : "original-warmup-window-closed", loadingPhase,
                "Original upstream warmup traversal remains running; per-admission estimate cap ms=" + budgetMilliseconds + "; no hard latency bound or scene delay.");
        }
    }
    void RetainLoadedShaders()
    {
        foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>()) retainedShaders.Add(shader);
    }
    void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (manualDependencies)
        {
            RetainLoadedShaders();
            Record("scene-loaded-awaiting-native-dependencies", loadingPhase, scene.path);
            return;
        }
        if (!scenes.TryGetValue(scene.path, out string phase)) return;
        RetainLoadedShaders();
        // Depth/offscreen rendering can occur during Awake, before this event.
        // Training phases are consecutive process intervals, never isolated scenes.
        var trace = PsoTraceController.Instance;
        if (trainingPhases && trace != null && trace.IsTracing)
        {
            trace.BeginPhase(phase, "external-scene-process-interval-not-isolated");
            Record("trace-boundary-after-scene-loaded", phase, "Earlier Awake/offscreen draws belong to the preceding interval.");
        }
        if (string.IsNullOrEmpty(revision)) revision = PsoUnityBuildIdentity.Capture().contentRevision;
        if (!requestedLoading)
        {
            requestedLoading = true;
            Request("process-loading", loadingPhase);
        }
        Request(scene.path, phase);
        Dispatch();
    }
    void Request(string content, string phase)
    {
        var request = lifecycle.Request(content, revision, phase);
        requests.Add(request);
        Record("dependencies-observed", phase, content, request);
    }
    void Dispatch()
    {
        foreach (var request in requests)
        {
            if (request.Status != PsoContentPhaseStatus.WaitingForDependencies) continue;
            lifecycle.DependenciesReady(request, renderCold);
            if (!lastStatus.TryGetValue(request.Generation, out var previous) || previous != request.Status)
            {
                Record("activation", request.Phase, request.Failure, request);
                lastStatus[request.Generation] = request.Status;
            }
        }
    }
    void Update()
    {
        if (quitting || lifecycle == null) return;
        // Bounded retries after a real scene load; normal rendering continues.
        if (Time.frameCount % 30 == 0)
        {
            RetainLoadedShaders();
            Dispatch();
            var pipeline = PsoWarmupOrchestrator.Instance;
            if (pipeline != null && pipeline.HasLoadedPlan && !pipeline.FeedbackTraceArmed) pipeline.TryArmFeedbackTrace();
        }
        var current = PsoWarmupOrchestrator.Instance;
        if (!reportedFeedbackArmed && current != null && current.FeedbackTraceArmed)
        {
            reportedFeedbackArmed = true;
            Record("feedback-observed-armed", loadingPhase, "Observed by adapter this frame; earlier draws may be outside the plan-baseline trace. Seeded baseline is not proof of draws or useful prewarming.");
        }
        lifecycle.Refresh();
        foreach (var request in requests)
            if (!lastStatus.TryGetValue(request.Generation, out var previous) || previous != request.Status)
            {
                Record("status", request.Phase, request.Failure, request);
                lastStatus[request.Generation] = request.Status;
            }
    }
    void Unloaded(Scene scene)
    {
        foreach (var request in requests)
        {
            if (request.ContentId != scene.path || request.Status == PsoContentPhaseStatus.Unloaded) continue;
            lifecycle.Unload(request);
            Record("scene-unload-retire", request.Phase, "Retained shader references remain until process exit; backend keeps each submitted collection until its fence.", request);
        }
    }
    void Record(string kind, string phase, string detail = null, PsoContentPhaseRequest request = null)
    {
        Debug.Log("[PSO External Phase] " + JsonUtility.ToJson(new Evidence { kind = kind, phase = phase,
            scene = request?.ContentId, status = request?.Status.ToString(), detail = detail,
            frame = Time.frameCount, seconds = Time.realtimeSinceStartupAsDouble,
            generation = request?.Generation ?? 0, retainedShaderCount = retainedShaders.Count }));
    }
    void OnApplicationQuit()
    {
        quitting = true;
        if (lifecycle == null) return;
        foreach (var request in requests) Record("quit-status", request.Phase, request.Failure, request);
        lifecycle.Dispose();
        // Do not clear shader references during OnApplicationQuit: a cancelled
        // native job may still own its fence until orchestrator shutdown completes.
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneUnloaded -= Unloaded;
    }
}
