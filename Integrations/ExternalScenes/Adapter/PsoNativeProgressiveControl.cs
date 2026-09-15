using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yanagisawa.ShaderHitchPipeline;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

// A direct public-API control. No scheduler/backend wrapper, feedback tracing,
// cost model, deadline estimate, extra scene load, or extension of the native route.
[DefaultExecutionOrder(-31850)]
public sealed class PsoNativeProgressiveControl : MonoBehaviour
{
    [Serializable] public sealed class Call
    {
        public string phase, api = "WarmUpProgressively", state;
        public int frame, count, completedBefore, completedAfter;
        public double startedSeconds, completedSeconds;
        public bool originalWarmupWindow, traceCacheMisses, shutdownFence;
    }
    [Serializable] public sealed class Phase
    {
        public string phase, collectionSha256;
        public int expectedStates, nativeStates, nativeCompleted;
        public bool loaded, warmed;
    }
    [Serializable] public sealed class Receipt
    {
        public int version = 1, fixedCountPerSubmission;
        public string backend = "unity-native-progressive-control-v1", planSha256, error;
        public string apiContract = "UnityEngine.Rendering.GraphicsStateCollection.WarmUpProgressively(int,JobHandle,bool=false)";
        public string admission = "At most one submission per Update; at most one outstanding job; only original native five-second Warming windows";
        public string countScope = "Unity native count units; no claim that collection entries are unique PSOs or cache misses";
        public bool normalQuit, allLoadedCollectionsWarmed;
        public Phase[] phases; public Call[] calls;
    }
    sealed class Owner
    {
        public PsoWarmupPhasePlan plan; public Phase evidence;
        public GraphicsStateCollection collection;
    }
    readonly List<Owner> owners = new List<Owner>();
    readonly List<Call> calls = new List<Call>();
    readonly List<Shader> retainedShaders = new List<Shader>();
    readonly Dictionary<string,string> scenePhases = new Dictionary<string,string>(StringComparer.Ordinal);
    Receipt receipt; string planDirectory, output; bool window, stopped, pending;
    int windowFrame, count; JobHandle job; Owner active; Call activeCall;

    public void Configure(string[] scenes, string[] phases, string loadingPhase)
    {
#if !UNITY_6000_5_OR_NEWER
        throw new InvalidOperationException("The direct native progressive control requires reviewed Unity 6000.5 APIs.");
#else
        var command = PsoCommandLine.Current;
        if (Application.unityVersion != "6000.5.9f1" || PsoWholeTaskCell.Capture() == null)
            throw new InvalidDataException("An explicit reviewed whole-task cell is required.");
        if (!int.TryParse(command.GetString("-pso-native-progressive-count", ""), out count) || count < 1 || count > 65536)
            throw new InvalidDataException("Provide one frozen positive native progressive count, at most 65536.");
        string path = command.GetString("-pso-native-progressive-plan", "");
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Explicit native control plan required.");
        var plan = PsoPlanValidation.LoadAndValidate(path, true, true);
        if (plan.compatibility == null) throw new InvalidDataException("Native control rejects unversioned legacy collection identity.");
        string[] expected = new[] { loadingPhase }.Concat(phases).ToArray();
        if (scenes.Length != phases.Length || !plan.phases.Select(p => p.phase).OrderBy(p => p).SequenceEqual(expected.OrderBy(p => p)))
            throw new InvalidDataException("Native control requires exactly the original loading and four route phases.");
        for (int i = 0; i < scenes.Length; i++) scenePhases.Add(scenes[i], phases[i]);
        planDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
        output = command.GetString(PsoConstants.OutputArgument, "");
        if (string.IsNullOrWhiteSpace(output) || File.Exists(Path.Combine(output, "native-progressive.json")))
            throw new InvalidDataException("A fresh explicit native control output is required.");
        receipt = new Receipt { fixedCountPerSubmission = count, planSha256 = plan.planSha256 };
        foreach (string phase in expected)
        {
            var item = plan.phases.Single(p => p.phase == phase);
            owners.Add(new Owner { plan = item, evidence = new Phase { phase = phase,
                collectionSha256 = item.collectionSha256, expectedStates = item.graphicsStateCount } });
        }
        SceneManager.sceneLoaded += Loaded;
#endif
    }

    public void ObserveOriginalWarmupWindow(bool isWarming)
    { window = isWarming; windowFrame = Time.frameCount; }

    void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (stopped || !scenePhases.TryGetValue(scene.path, out string phase)) return;
        try
        {
            foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
                if (shader != null && !retainedShaders.Contains(shader)) retainedShaders.Add(shader);
            Load(owners[0]);
            Load(owners.Single(o => o.plan.phase == phase));
        }
        catch (Exception error) { Fail(error); }
    }
    void Load(Owner owner)
    {
        if (owner.collection != null) return;
        string path = PsoFileUtility.ResolveChildPath(planDirectory, owner.plan.collectionFile);
        if (!string.Equals(PsoFileUtility.ComputeSha256(path), owner.plan.collectionSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Collection changed after native control plan validation.");
        var collection = new GraphicsStateCollection();
        owner.collection = collection; // Own even a failing LoadFromFile until cleanup.
        if (!collection.LoadFromFile(path) || collection.runtimePlatform != Application.platform ||
            collection.graphicsDeviceType != SystemInfo.graphicsDeviceType || collection.totalGraphicsStateCount != owner.plan.graphicsStateCount)
            throw new InvalidDataException("Native collection failed load/platform/API/count validation.");
        owner.evidence.loaded = true;
        owner.evidence.nativeStates = collection.totalGraphicsStateCount;
        Refresh(owner);
    }
    void Update()
    {
        try
        {
            if (pending && job.IsCompleted) Complete(false);
            if (stopped || pending || !window || windowFrame != Time.frameCount) return;
            var owner = owners.FirstOrDefault(o => o.evidence.loaded && !o.collection.isWarmedUp);
            if (owner == null) return;
            active = owner;
            activeCall = new Call { phase = owner.plan.phase, frame = Time.frameCount, count = count,
                completedBefore = owner.collection.completedWarmupCount, startedSeconds = Time.realtimeSinceStartupAsDouble,
                originalWarmupWindow = true, state = "submitting" };
            calls.Add(activeCall);
#if UNITY_6000_5_OR_NEWER
            job = owner.collection.WarmUpProgressively(count, default(JobHandle), false);
#else
            throw new InvalidOperationException("Unsupported native API generation.");
#endif
            pending = true; activeCall.state = "submitted";
        }
        catch (Exception error) { Fail(error); }
    }
    void Complete(bool shutdown)
    {
        job.Complete(); // In Update only after IsCompleted; shutdown fence is separately recorded.
        activeCall.completedAfter = active.collection.completedWarmupCount;
        activeCall.completedSeconds = Time.realtimeSinceStartupAsDouble;
        activeCall.shutdownFence = shutdown; activeCall.state = "completed";
        Refresh(active); pending = false;
    }
    static void Refresh(Owner owner)
    {
        owner.evidence.nativeCompleted = owner.collection.completedWarmupCount;
        owner.evidence.warmed = owner.collection.isWarmedUp;
    }
    void Fail(Exception error)
    {
        stopped = true;
        if (receipt != null) receipt.error = error.GetType().Name + ": " + error.Message;
        Debug.LogException(error);
    }
    void Finish(bool normalQuit)
    {
        if (receipt == null) return;
        stopped = true;
        try { if (pending) Complete(true); }
        catch (Exception error) { Fail(error); }
        receipt.normalQuit = normalQuit;
        receipt.phases = owners.Select(o => o.evidence).ToArray(); receipt.calls = calls.ToArray();
        receipt.allLoadedCollectionsWarmed = owners.All(o => o.evidence.loaded && o.evidence.warmed);
        File.WriteAllText(Path.Combine(output, "native-progressive.json"), JsonUtility.ToJson(receipt, true));
        // A failed fence must keep ownership; never destroy a collection still in native use.
        if (!pending) foreach (var owner in owners) if (owner.collection != null) Destroy(owner.collection);
        receipt = null;
    }
    void OnApplicationQuit() => Finish(true);
    void OnDestroy() { SceneManager.sceneLoaded -= Loaded; Finish(false); }
}
