using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using Yanagisawa.ShaderHitchPipeline;

// Opt-in diagnosis, never enabled by the ordinary external capture flag.
// Marker work summed over threads is not wall time, GPU time or proof of a stall.
[DefaultExecutionOrder(-31890)]
public sealed class PsoWholeTaskProfiler : MonoBehaviour
{
    public static readonly Guid MetadataId = new Guid("479e179b-3aa6-4330-94aa-9f14b66d8afd");
    public const int FrameTag = 1;
    [Serializable] public sealed class Metric
    {
        public string name, category, unit, error;
        public bool available;
    }
    [Serializable] public sealed class Sample
    {
        public int observedAtUnityFrame, precedingUnityFrame;
        public double realtimeSeconds;
        public long[] values, counts;
    }
    [Serializable] public sealed class Receipt
    {
        public int schemaVersion=1;
        public string unityVersion, startedUtc, finishedUtc, binaryProfile;
        public bool diagnosticOnly=true, normalQuit;
        public string scope="Last completed profiler-frame aggregates across threads, observed at Update. Nested/parallel markers overlap; do not add them or equate them to CPU interval, GPU completion or presentation. Binary frame metadata is the authoritative exact join. Missing markers are unavailable, never zero evidence.";
        public Metric[] metrics;
        public Sample[] samples;
    }
    static readonly string[] Names={ "Shader.CreateGPUProgram", "CreateGraphicsGraphicsPipelineImpl",
        "CreateGraphicsPipelineImpl", "CreateComputePipelineImpl", "Shader.Parse", "Loading.AwakeFromLoad",
        "Loading.ReadObject", "PreloadManager", "AsyncReadManager.ReadFile", "WaitForJobGroupID",
        "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "GC.Collect", "PlayerLoop" };
    readonly List<Sample> samples=new List<Sample>(65536);
    readonly List<Metric> metrics=new List<Metric>();
    readonly List<ProfilerRecorder> recorders=new List<ProfilerRecorder>();
    readonly int[] frameMetadata=new int[1];
    Receipt receipt;
    string output;
    bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void Bootstrap()
    {
        if (!PsoCommandLine.Current.HasFlag("-pso-whole-task-profile")) return;
        var host=new GameObject("PSO whole task diagnostic profiler");
        DontDestroyOnLoad(host); host.AddComponent<PsoWholeTaskProfiler>();
    }
    void Awake()
    {
        if (!Debug.isDebugBuild) throw new InvalidOperationException("Whole-task binary diagnosis requires a Development Player.");
        output=PsoCommandLine.Current.GetString(PsoConstants.OutputArgument,"");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Explicit output required.");
        Directory.CreateDirectory(output);
        string path=Path.Combine(output,"whole-task-profiler.json");
        string binary=Path.Combine(output,"whole-task.raw");
        if (File.Exists(path) || File.Exists(binary)) throw new IOException("Keep previous diagnostic evidence.");
        receipt=new Receipt { unityVersion=Application.unityVersion,startedUtc=DateTime.UtcNow.ToString("o"),binaryProfile=binary };
        Profiler.logFile=binary;
        Profiler.maxUsedMemory=512*1024*1024;
        Profiler.enableBinaryLog=true;
        Profiler.enabled=true;
        var handles=new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (string name in Names)
        {
            var metric=new Metric { name=name,error="Marker not registered at bootstrap; availability not established." };
            var recorder=default(ProfilerRecorder);
            foreach (var handle in handles)
            {
                var description=ProfilerRecorderHandle.GetDescription(handle);
                if (description.Name!=name) continue;
                metric.category=description.Category.Name; metric.unit=description.UnitType.ToString();
                try
                {
                    recorder=new ProfilerRecorder(handle,1,ProfilerRecorderOptions.StartImmediately |
                        ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame);
                    metric.available=recorder.Valid; metric.error=recorder.Valid?null:"Recorder invalid.";
                }
                catch (Exception error) { metric.error=error.ToString(); }
                break;
            }
            metrics.Add(metric); recorders.Add(recorder);
        }
    }
    void Update()
    {
        frameMetadata[0]=Time.frameCount;
        Profiler.EmitFrameMetaData(MetadataId,FrameTag,frameMetadata);
        var sample=new Sample { observedAtUnityFrame=Time.frameCount,precedingUnityFrame=Time.frameCount-1,
            realtimeSeconds=Time.realtimeSinceStartupAsDouble,values=new long[recorders.Count],counts=new long[recorders.Count] };
        for (int i=0;i<recorders.Count;++i)
        {
            var recorder=recorders[i];
            if (!recorder.Valid || recorder.Count==0) { sample.values[i]=-1;sample.counts[i]=-1;continue; }
            var value=recorder.GetSample(recorder.Count-1);
            sample.values[i]=value.Value;sample.counts[i]=value.Count;
        }
        samples.Add(sample);
    }
    void OnApplicationQuit()
    {
        if (finished || receipt==null) return;
        finished=true;receipt.normalQuit=true;receipt.finishedUtc=DateTime.UtcNow.ToString("o");
        receipt.metrics=metrics.ToArray();receipt.samples=samples.ToArray();
        try { File.WriteAllText(Path.Combine(output,"whole-task-profiler.json"),JsonUtility.ToJson(receipt)); }
        finally { StopOwnedProfiler(); }
    }
    void StopOwnedProfiler()
    {
        Profiler.enabled=false;Profiler.enableBinaryLog=false;
        foreach (var recorder in recorders) if (recorder.Valid) recorder.Dispose();
        recorders.Clear();
    }
    void OnDestroy() { if (receipt!=null) StopOwnedProfiler(); }
}
