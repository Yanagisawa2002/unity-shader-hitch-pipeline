using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Benchmarking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Yanagisawa.ShaderHitchPipeline;
using Yanagisawa.ShaderHitchPipeline.Editor;

// Additive host-only adapter. Uses the original serialized BenchmarkScene, not
// the bare prefab's stale scene names. Keeps all original desktop quality assets.
public static class PsoUrpSampleBuild
{
    public static readonly string[] Scenes = {
        "Assets/SharedAssets/Benchmark/BenchmarkScene.unity",
        "Assets/Scenes/Terminal/TerminalScene.unity",
        "Assets/Scenes/Oasis/OasisScene.unity",
        "Assets/Scenes/Garden/GardenScene.unity",
        "Assets/Scenes/Cockpit/CockpitScene.unity" };
    public static void Configure()
    {
        ValidateVersion();
        var scene=EditorSceneManager.OpenScene(Scenes[0],OpenSceneMode.Single);
        var benchmark=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PerformanceTest>(true)).Single();
        if (!benchmark._stages.Select(s=>s.sceneName).SequenceEqual(new[] { "TerminalScene","GardenScene","OasisScene","CockpitScene" }) ||
            benchmark._stages.Any(s=>!s.enabled || !s.useFullTimeline) || benchmark._waitTime!=5 || benchmark._framesToCapture!=500 || benchmark.liveRefreshGraph)
            throw new BuildFailedException("Preserve all four original resolved benchmark stages, full timelines and wait.");
        if (QualitySettings.names.Length!=4 || QualitySettings.names[3]!="PC High" || QualitySettings.GetQualityLevel()!=3)
            throw new BuildFailedException("Unexpected original PC High default quality.");
        EditorBuildSettings.scenes=Scenes.Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[] { GraphicsDeviceType.Direct3D12 });
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.IL2CPP);
        PlayerSettings.runInBackground=true;
        new PsoProjectConfiguration { profileId="urp-sample-17.1.5-6000.1-d3d12-pchigh",
            targetQualityLevelName="PC High", preinteractiveBootstrap=false,
            deferredDeadlineMilliseconds=1000, deferredExpectedUseProbability=1 }.Save();
        // No upstream scene/prefab/material/timeline save or route modification.
        AssetDatabase.SaveAssets();
        Debug.Log("[PSO URP] Configured original BenchmarkScene; Terminal/Garden/Oasis/Cockpit, PC High, D3D12/IL2CPP.");
    }
    public static void BuildPlayer()
    {
        ValidateVersion();
        string output=PsoCommandLine.Current.GetString("-pso-native-build-output","");
        if (string.IsNullOrWhiteSpace(output) || Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
            throw new BuildFailedException("Require a new Player output directory.");
        if (!EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).SequenceEqual(Scenes) ||
            PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)!=ScriptingImplementation.IL2CPP ||
            PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64) ||
            !PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64).SequenceEqual(new[] { GraphicsDeviceType.Direct3D12 }))
            throw new BuildFailedException("Wrong external build cell.");
        var options=new BuildPlayerOptions { scenes=Scenes,locationPathName=output,target=BuildTarget.StandaloneWindows64,
            subtarget=(int)StandaloneBuildSubtarget.Player,options=BuildOptions.Development };
        PsoBuildIdentityCapture.DeclareBuildInputs(options);
        var report=BuildPipeline.BuildPlayer(options);
        string receipt=PsoCommandLine.Current.GetString("-pso-build-receipt","");
        if (!string.IsNullOrWhiteSpace(receipt)) File.WriteAllText(receipt,JsonUtility.ToJson(new Receipt {
            result=report.summary.result.ToString(),guid=report.summary.guid.ToString(),bytes=report.summary.totalSize,
            seconds=report.summary.totalTime.TotalSeconds,errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,scenes=Scenes },true));
        if (report.summary.result!=BuildResult.Succeeded) throw new BuildFailedException("URP Player build failed.");
    }
    public static void ProcessInbox()
    {
        ValidateVersion();
        var retained=new List<UnityEngine.Object>();
        foreach (string guid in AssetDatabase.FindAssets("t:Shader t:Material"))
            retained.Add(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
        PsoBatch.ProcessInbox();
        GC.KeepAlive(retained);
    }
    static void ValidateVersion()
    {
        if (Application.unityVersion!="6000.1.0f1") throw new BuildFailedException("Locked Editor required.");
    }
    [Serializable] sealed class Receipt
    {
        public string result,guid; public ulong bytes; public double seconds;
        public int errors,warnings; public string[] scenes;
    }
}
