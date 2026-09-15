#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using Yanagisawa.ShaderHitchPipeline;

// Offline extraction from retained native data. No reflection, UI, scene edits,
// fake frame joins or summed parallel-time speedup. The .raw remains authoritative.
public static class PsoWholeTaskProfilerExport
{
    [Serializable] sealed class Frame
    {
        public string kind="frame";
        public int profileFrame,unityFrame,threads;
        public double mainThreadFrameMilliseconds;
        public ulong startNanoseconds;
    }
    [Serializable] sealed class Sample
    {
        public string kind="sample",thread,group,name,flags;
        public int profileFrame,unityFrame,threadIndex,sampleIndex,parentSample;
        public ulong startNanoseconds,durationNanoseconds;
        public string[] ancestors;
    }
    [Serializable] sealed class Flow
    {
        public string kind="flow",type;
        public int profileFrame,threadIndex,parentSample;
        public uint id;
    }
    [Serializable] sealed class Summary
    {
        public int schemaVersion=1,firstProfileFrame,lastProfileFrame,exportedFrames,joinedFrames;
        public string input,inputSha256,unityVersion,startedUtc,finishedUtc;
        public string scope="Offline CPU profiler samples. Frame joins use emitted metadata, -1 means unavailable. Details include shader/pipeline samples and >=0.5 ms samples from >33.333 ms main-thread frames. Binary file retains all samples. Nested or parallel durations overlap. No automatic causal, GPU or presentation claim; import coverage must be compared with the full recorder/capture.";
        public string[] observedShaderMarkers;
    }
    static bool ShaderMarker(string name) => name.IndexOf("Shader",StringComparison.OrdinalIgnoreCase)>=0 ||
        name.IndexOf("PipelineImpl",StringComparison.OrdinalIgnoreCase)>=0 ||
        name.IndexOf("PipelineState",StringComparison.OrdinalIgnoreCase)>=0;

    public static void Export()
    {
        string input=PsoCommandLine.Current.GetString("-pso-profile-input","");
        string output=PsoCommandLine.Current.GetString("-pso-profile-output","");
        if (!File.Exists(input) || string.IsNullOrWhiteSpace(output) || Directory.Exists(output))
            throw new InvalidOperationException("Existing native profile and new output directory required.");
        Directory.CreateDirectory(output);
        var summary=new Summary { input=input,inputSha256=PsoFileUtility.ComputeSha256(input),unityVersion=Application.unityVersion,
            startedUtc=DateTime.UtcNow.ToString("o") };
        ProfilerDriver.ClearAllFrames();
        if (!ProfilerDriver.LoadProfile(input,false)) throw new InvalidDataException("Native profiler import failed.");
        summary.firstProfileFrame=ProfilerDriver.firstFrameIndex;summary.lastProfileFrame=ProfilerDriver.lastFrameIndex;
        if (summary.firstProfileFrame<0 || summary.lastProfileFrame<summary.firstProfileFrame)
            throw new InvalidDataException("Native profile has no usable frames.");
        var markers=new HashSet<string>();
        using (var writer=new StreamWriter(Path.Combine(output,"samples.jsonl"),false))
        {
            for (int f=summary.firstProfileFrame;f<=summary.lastProfileFrame;++f)
            {
                Frame frame;
                using (var main=ProfilerDriver.GetRawFrameDataView(f,0))
                {
                    if (!main.valid) continue;
                    int unityFrame=-1;
                    if (main.GetFrameMetaDataCount(PsoWholeTaskProfiler.MetadataId,PsoWholeTaskProfiler.FrameTag)>0)
                    {
                        var data=main.GetFrameMetaData<int>(PsoWholeTaskProfiler.MetadataId,PsoWholeTaskProfiler.FrameTag);
                        if (data.Length==1) unityFrame=data[0];
                    }
                    frame=new Frame { profileFrame=f,unityFrame=unityFrame,mainThreadFrameMilliseconds=main.frameTimeMs,
                        startNanoseconds=main.frameStartTimeNs };
                }
                summary.exportedFrames++; if (frame.unityFrame>=0) summary.joinedFrames++;
                for (int t=0;t<1024;++t)
                using (var data=ProfilerDriver.GetRawFrameDataView(f,t))
                {
                    if (!data.valid) break;
                    frame.threads++;
                    var parents=new List<(int index,int end,string name)>();
                    for (int s=0;s<data.sampleCount;++s)
                    {
                        while (parents.Count>0 && parents[parents.Count-1].end<s) parents.RemoveAt(parents.Count-1);
                        string name=data.GetSampleName(s);
                        ulong duration=data.GetSampleTimeNs(s);
                        bool shader=ShaderMarker(name);
                        if (shader) markers.Add(name);
                        if (shader || (frame.mainThreadFrameMilliseconds>33.333 && duration>=500000))
                            writer.WriteLine(JsonUtility.ToJson(new Sample { profileFrame=f,unityFrame=frame.unityFrame,
                                threadIndex=t,thread=data.threadName,group=data.threadGroupName,sampleIndex=s,name=name,
                                flags=data.GetSampleFlags(s).ToString(),startNanoseconds=data.GetSampleStartTimeNs(s),durationNanoseconds=duration,
                                parentSample=parents.Count==0?-1:parents[parents.Count-1].index,ancestors=parents.Select(p=>p.name).ToArray() }));
                        int children=data.GetSampleChildrenCountRecursive(s);
                        if (children>0) parents.Add((s,s+children,name));
                    }
                    var flows=new List<RawFrameDataView.FlowEvent>();data.GetFlowEvents(flows);
                    foreach (var flow in flows)
                        writer.WriteLine(JsonUtility.ToJson(new Flow { profileFrame=f,threadIndex=t,parentSample=flow.ParentSampleIndex,
                            id=flow.FlowId,type=flow.FlowEventType.ToString() }));
                }
                writer.WriteLine(JsonUtility.ToJson(frame));
            }
        }
        summary.observedShaderMarkers=markers.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        summary.finishedUtc=DateTime.UtcNow.ToString("o");
        File.WriteAllText(Path.Combine(output,"export.json"),JsonUtility.ToJson(summary,true));
        Debug.Log("[PSO Whole Task] Offline frames="+summary.exportedFrames+"; joined="+summary.joinedFrames);
    }
}
#endif
