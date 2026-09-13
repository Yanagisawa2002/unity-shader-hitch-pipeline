using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Benchmarking;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Yanagisawa.ShaderHitchPipeline;

// Observes the official benchmark. Never calls its Start/Cancel/Close methods,
// changes cameras/timelines, loads a scene, generates content, or sends input.
// The only route-completion action is normal Application.Quit after native CSV
// publication and all four original stages reporting Finished.
[DefaultExecutionOrder(-31900)]
public sealed class PsoUrpSampleCapture : MonoBehaviour
{
    public static readonly string[] ScenePaths = {
        "Assets/Scenes/Terminal/TerminalScene.unity", "Assets/Scenes/Garden/GardenScene.unity",
        "Assets/Scenes/Oasis/OasisScene.unity", "Assets/Scenes/Cockpit/CockpitScene.unity" };
    [Serializable] public struct Frame
    {
        public int frame;
        public string scene, stage, status;
        public double seconds, updateIntervalMilliseconds;
        public long allocatedBytes,reservedBytes;
    }
    [Serializable] public struct Render
    {
        public int frame,cameraId,pixelWidth,pixelHeight;
        public string scene,stage,status,camera,cameraType,director,timelineAsset,timelineState;
        public string cameraAssociation,activeVirtualCamera,animatedTarget,animationTrack;
        public double seconds,timelineTime,timelineDuration;
        public Vector3 position; public Quaternion rotation;
        public bool targetTexture,timelineBoundToRenderingCamera;
    }
    [Serializable] public struct Event
    {
        public string kind,scene,detail;
        public int frame,rootObjects;
        public double seconds;
    }
    [Serializable] public struct TimelineEvent
    {
        public string kind,scene,stage,status,director,asset,state,wrapMode;
        public int frame;
        public double seconds,time,duration;
    }
    [Serializable] public struct BindingEvidence
    {
        public string scene,director,asset,track,target,type;
        public int frame;
    }
    [Serializable] public sealed class Capture
    {
        public int schemaVersion=2;
        public string startedUtc,finishedUtc,unityVersion,buildGuid,graphicsApi,gpu,quality,culture;
        public string timingScope="CPU Update intervals and endCameraRendering submissions, not GPU completion/presentation. Original first-load and 5-second warmup included; native CSV measures warmed full timelines separately.";
        public bool applicationQuit,screenshotsEnabled,originalBenchmarkFinished;
        public int width,height,errors,exceptions,csvPublications,vSyncCount,targetFrameRate;
        public double elapsedSeconds,observerRealtimeOriginSeconds;
        public Frame[] frames; public Render[] renders; public Event[] events;
        public TimelineEvent[] timelineEvents; public BindingEvidence[] bindings;
    }
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly List<Frame> frames=new List<Frame>(65536);
    readonly List<Render> renders=new List<Render>(65536);
    readonly List<Event> events=new List<Event>();
    readonly List<TimelineEvent> timelineEvents=new List<TimelineEvent>();
    readonly List<BindingEvidence> bindings=new List<BindingEvidence>();
    readonly HashSet<(int,int,int)> recordedBindings=new HashSet<(int,int,int)>();
    readonly Dictionary<int,string> hierarchyPaths=new Dictionary<int,string>();
    readonly Dictionary<string,TestStageStatus> statuses=new Dictionary<string,TestStageStatus>();
    readonly HashSet<string> screenshots=new HashSet<string>();
    PlayableDirector[] directors=Array.Empty<PlayableDirector>();
    Capture capture; PsoExternalPhaseBridge bridge;
    string output,upstreamCsv;
    double previousUpdate;
    int quitAtFrame=-1;
    bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void Bootstrap()
    {
        if (!PsoCommandLine.Current.HasFlag("-pso-external-capture")) return;
        var host=new GameObject("PSO official URP sample observer");
        DontDestroyOnLoad(host); host.AddComponent<PsoUrpSampleCapture>();
    }
    void Awake()
    {
        output=PsoCommandLine.Current.GetString(PsoConstants.OutputArgument,"");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Explicit capture output required.");
        Directory.CreateDirectory(output);
        if (File.Exists(Path.Combine(output,"external-capture.json"))) throw new IOException("Capture already exists.");
        capture=new Capture { startedUtc=DateTime.UtcNow.ToString("o"),unityVersion=Application.unityVersion,
            buildGuid=Application.buildGUID,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),gpu=SystemInfo.graphicsDeviceName,
            culture=CultureInfo.CurrentCulture.Name,screenshotsEnabled=PsoCommandLine.Current.HasFlag("-pso-external-screenshots") };
        capture.observerRealtimeOriginSeconds=Time.realtimeSinceStartupAsDouble-clock.Elapsed.TotalSeconds;
        SceneManager.sceneLoaded+=Loaded; SceneManager.sceneUnloaded+=Unloaded;
        RenderPipelineManager.endCameraRendering+=Rendered;
        Application.logMessageReceivedThreaded+=Message;
        PsoExternalPhaseBridge.Attach(gameObject,ScenePaths,new[] { "urp-terminal","urp-garden","urp-oasis","urp-cockpit" },"urp-loading");
        bridge=GetComponent<PsoExternalPhaseBridge>();
        AddEvent("observer-before-splash",default,"");
    }
    void Loaded(Scene scene,LoadSceneMode mode)
    {
        AddEvent("scene-loaded-not-first-draw",scene,"");
        hierarchyPaths.Clear();
        directors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayableDirector>(true)).ToArray();
        foreach (var director in directors)
        {
            AddEvent("original-scene-director",scene,director.name+" / "+(director.playableAsset==null?"<none>":director.playableAsset.name));
            director.played+=Played; director.stopped+=Stopped; director.paused+=Paused;
        }
    }
    void Unloaded(Scene scene) => AddEvent("scene-unloaded",scene,"");
    void AddEvent(string kind,Scene scene,string detail)
    {
        var value=new Event { kind=kind,scene=scene.IsValid()?scene.path:"",detail=detail,frame=Time.frameCount,
            seconds=clock.Elapsed.TotalSeconds,rootObjects=scene.IsValid() && scene.isLoaded?scene.rootCount:0 };
        events.Add(value); UnityEngine.Debug.Log("[PSO URP Observer] "+JsonUtility.ToJson(value));
    }
    PerformanceTestStage ActiveStage()
    {
        if (!PerformanceTest.RunningBenchmark) return null;
        return PerformanceTest.instance._stages.FirstOrDefault(s=>s.status==TestStageStatus.Warming || s.status==TestStageStatus.Running);
    }
    void Update()
    {
        double now=clock.Elapsed.TotalSeconds;
        var active=ActiveStage();
        frames.Add(new Frame { frame=Time.frameCount,seconds=now,updateIntervalMilliseconds=frames.Count==0?0:(now-previousUpdate)*1000,
            scene=SceneManager.GetActiveScene().path,stage=active?.sceneName??"",status=active?.status.ToString()??"",
            allocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),reservedBytes=Profiler.GetTotalReservedMemoryLong() });
        previousUpdate=now;
        bridge.ObserveOriginalWarmupWindow(active!=null && active.status==TestStageStatus.Warming,1000);
        if (PerformanceTest.RunningBenchmark)
        {
            var stages=PerformanceTest.instance._stages;
            foreach (var stage in stages)
                if (!statuses.TryGetValue(stage.sceneName,out var old) || old!=stage.status)
                {
                    statuses[stage.sceneName]=stage.status;
                    AddEvent("original-stage-status",SceneManager.GetActiveScene(),stage.sceneName+":"+stage.status);
                    foreach (var director in directors)
                        if (director!=null) RecordTimeline("stage-"+stage.sceneName+"-"+stage.status,director);
                }
            if (quitAtFrame<0 && upstreamCsv!=null && stages.Count==4 && stages.All(s=>s.enabled && s.useFullTimeline && s.status==TestStageStatus.Finished))
            {
                capture.originalBenchmarkFinished=true;
                AddEvent("original-full-csv-and-four-stages-finished",SceneManager.GetActiveScene(),"Normal adapter exit after native completion; no original UI action invoked.");
                quitAtFrame=Time.frameCount+2;
            }
        }
        if (quitAtFrame>=0 && Time.frameCount>=quitAtFrame) Application.Quit(0);
    }
    string Hierarchy(Transform value)
    {
        if (value==null) return "";
        int id=value.GetInstanceID();
        if (!hierarchyPaths.TryGetValue(id,out string path))
        {
            path=value.parent==null?value.name:Hierarchy(value.parent)+"/"+value.name;
            hierarchyPaths[id]=path;
        }
        return path;
    }
    void Played(PlayableDirector director) => RecordTimeline("played",director);
    void Stopped(PlayableDirector director) => RecordTimeline("stopped",director);
    void Paused(PlayableDirector director) => RecordTimeline("paused",director);
    void RecordTimeline(string kind,PlayableDirector director)
    {
        var stage=ActiveStage();
        var value=new TimelineEvent { kind=kind,scene=director.gameObject.scene.path,stage=stage?.sceneName??"",
            status=stage?.status.ToString()??"",director=director.name,asset=director.playableAsset?.name??"",
            frame=Time.frameCount,seconds=clock.Elapsed.TotalSeconds,time=director.time,duration=director.duration,
            state=director.state.ToString(),wrapMode=director.extrapolationMode.ToString() };
        timelineEvents.Add(value);
        UnityEngine.Debug.Log("[PSO URP Timeline] "+JsonUtility.ToJson(value));
    }
    PlayableDirector BoundDirector(Camera camera,out string association,out string virtualPath,out string targetPath,out string track)
    {
        association=virtualPath=targetPath=track="";
        var brain=camera.GetComponent<CinemachineBrain>();
        var live=brain!=null && brain.isActiveAndEnabled && brain.OutputCamera==camera
            ?brain.ActiveVirtualCamera as CinemachineVirtualCameraBase:null;
        if (live!=null) virtualPath=Hierarchy(live.transform);
        foreach (var director in directors)
        {
            if (director==null || director.playableAsset==null) continue;
            foreach (var binding in director.playableAsset.outputs)
            {
                var component=director.GetGenericBinding(binding.sourceObject) as Component;
                if (component==null) continue;
                var key=(director.GetInstanceID(),binding.sourceObject.GetInstanceID(),component.GetInstanceID());
                if (recordedBindings.Add(key)) bindings.Add(new BindingEvidence { scene=director.gameObject.scene.path,
                    director=director.name,asset=director.playableAsset.name,track=binding.streamName,
                    target=Hierarchy(component.transform),type=component.GetType().FullName,frame=Time.frameCount });
                // Other routes bind the rendering camera's Brain directly. Cockpit's
                // original Animation track binds the ancestor of the *actually live*
                // virtual camera. Do not infer association merely from co-movement.
                if (component.gameObject==camera.gameObject)
                    association="direct-render-camera-binding";
                else if (component is Animator && live!=null && live.transform.IsChildOf(component.transform))
                    association="live-cinemachine-camera-under-timeline-animator";
                else if (component is Animator && live!=null && live.Follow!=null && live.Follow.IsChildOf(component.transform))
                    association="live-cinemachine-follow-under-timeline-animator";
                else continue;
                targetPath=Hierarchy(component.transform);track=binding.streamName;
                    return director;
            }
        }
        return null;
    }
    void Rendered(ScriptableRenderContext context,Camera camera)
    {
        if (camera==null || camera.cameraType!=CameraType.Game) return;
        var active=ActiveStage(); var director=BoundDirector(camera,out string association,out string live,out string target,out string track);
        var value=new Render { frame=Time.frameCount,seconds=clock.Elapsed.TotalSeconds,scene=SceneManager.GetActiveScene().path,
            stage=active?.sceneName??"",status=active?.status.ToString()??"",camera=camera.name,cameraId=camera.GetInstanceID(),
            cameraType=camera.cameraType.ToString(),pixelWidth=camera.pixelWidth,pixelHeight=camera.pixelHeight,targetTexture=camera.targetTexture!=null,
            position=camera.transform.position,rotation=camera.transform.rotation,
            cameraAssociation=association,activeVirtualCamera=live,animatedTarget=target,animationTrack=track,
            director=director==null?"":director.name,timelineAsset=director==null?"":director.playableAsset.name,
            timelineTime=director==null?-1:director.time,timelineDuration=director==null?0:director.duration,
            timelineState=director==null?"":director.state.ToString(),timelineBoundToRenderingCamera=director!=null };
        renders.Add(value);
        if (!capture.screenshotsEnabled || camera.targetTexture!=null || director==null || active==null) return;
        bool warmup=active.status==TestStageStatus.Warming && director.time>=1;
        bool middle=active.status==TestStageStatus.Running && director.time>=director.duration*.5;
        if (warmup || middle)
        {
            string key=active.sceneName+(warmup?"-original-warmup":"-timeline-middle");
            if (screenshots.Add(key))
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(output,key+".png"));
                AddEvent("rendered-screenshot-request",SceneManager.GetActiveScene(),key);
            }
        }
    }
    void Message(string message,string stack,LogType type)
    {
        if (type==LogType.Error || type==LogType.Assert) Interlocked.Increment(ref capture.errors);
        if (type==LogType.Exception) Interlocked.Increment(ref capture.exceptions);
        if (message.StartsWith("URP Template Performance Test",StringComparison.Ordinal))
        {
            Interlocked.Increment(ref capture.csvPublications);
            Interlocked.CompareExchange(ref upstreamCsv,message,null);
        }
    }
    void OnApplicationQuit()
    {
        if (finished || capture==null) return;
        finished=true; AddEvent("application-quit",SceneManager.GetActiveScene(),"");
        capture.applicationQuit=true; capture.finishedUtc=DateTime.UtcNow.ToString("o");capture.elapsedSeconds=clock.Elapsed.TotalSeconds;
        capture.quality=QualitySettings.names[QualitySettings.GetQualityLevel()];capture.width=Screen.width;capture.height=Screen.height;
        capture.vSyncCount=QualitySettings.vSyncCount;capture.targetFrameRate=Application.targetFrameRate;
        capture.frames=frames.ToArray();capture.renders=renders.ToArray();capture.events=events.ToArray();
        capture.timelineEvents=timelineEvents.ToArray();capture.bindings=bindings.ToArray();
        File.WriteAllText(Path.Combine(output,"external-capture.json"),JsonUtility.ToJson(capture));
        if (upstreamCsv!=null) File.WriteAllText(Path.Combine(output,"upstream-results.csv"),upstreamCsv);
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded-=Loaded;SceneManager.sceneUnloaded-=Unloaded;
        RenderPipelineManager.endCameraRendering-=Rendered;Application.logMessageReceivedThreaded-=Message;
        foreach (var director in directors)
            if (director!=null) { director.played-=Played;director.stopped-=Stopped;director.paused-=Paused; }
    }
}
