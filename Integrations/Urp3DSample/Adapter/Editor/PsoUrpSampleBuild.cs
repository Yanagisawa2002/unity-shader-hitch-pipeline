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
        BuildTarget target=Target(out GraphicsDeviceType api);
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,target))
            throw new BuildFailedException("Selected standalone support module is not installed.");
        var scene=EditorSceneManager.OpenScene(Scenes[0],OpenSceneMode.Single);
        var benchmark=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PerformanceTest>(true)).Single();
        if (!benchmark._stages.Select(s=>s.sceneName).SequenceEqual(new[] { "TerminalScene","GardenScene","OasisScene","CockpitScene" }) ||
            benchmark._stages.Any(s=>!s.enabled || !s.useFullTimeline) || benchmark._waitTime!=5 || benchmark._framesToCapture!=500 || benchmark.liveRefreshGraph)
            throw new BuildFailedException("Preserve all four original resolved benchmark stages, full timelines and wait.");
        if (QualitySettings.names.Length!=4 || QualitySettings.names[3]!="PC High" || QualitySettings.GetQualityLevel()!=3)
            throw new BuildFailedException("Unexpected original PC High default quality.");
        EditorBuildSettings.scenes=Scenes.Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
        PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
        PlayerSettings.SetGraphicsAPIs(target,new[] { api });
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.IL2CPP);
        PlayerSettings.runInBackground=true;
        string engineCell = Application.unityVersion == "6000.1.0f1" ? "6000.1" : Application.unityVersion;
        string cellName=api==GraphicsDeviceType.Direct3D12 ? "d3d12" : "linux-vulkan";
        new PsoProjectConfiguration { profileId="urp-sample-17.1.5-"+engineCell+"-"+cellName+"-pchigh",
            targetRuntimePlatform=target==BuildTarget.StandaloneLinux64 ? "LinuxPlayer" : "WindowsPlayer",
            targetGraphicsDeviceType=api.ToString(),
            targetQualityLevelName="PC High", preinteractiveBootstrap=false,
            deferredDeadlineMilliseconds=1000, deferredExpectedUseProbability=1 }.Save();
        // No upstream scene/prefab/material/timeline save or route modification.
        AssetDatabase.SaveAssets();
        Debug.Log("[PSO URP] Configured original BenchmarkScene; Terminal/Garden/Oasis/Cockpit, PC High, "+target+"/"+api+"/IL2CPP.");
    }
    public static void BuildPlayer()
    {
        ValidateVersion();
        BuildTarget target=Target(out GraphicsDeviceType api);
        string output=PsoCommandLine.Current.GetString("-pso-native-build-output","");
        if (string.IsNullOrWhiteSpace(output) || Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
            throw new BuildFailedException("Require a new Player output directory.");
        if (!EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).SequenceEqual(Scenes) ||
            PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)!=ScriptingImplementation.IL2CPP ||
            PlayerSettings.GetUseDefaultGraphicsAPIs(target) ||
            !PlayerSettings.GetGraphicsAPIs(target).SequenceEqual(new[] { api }))
            throw new BuildFailedException("Wrong external build cell.");
        var options=new BuildPlayerOptions { scenes=Scenes,locationPathName=output,target=target,
            subtarget=(int)StandaloneBuildSubtarget.Player,options=BuildOptions.Development };
        PsoBuildIdentityCapture.DeclareBuildInputs(options);
        var report=BuildPipeline.BuildPlayer(options);
        string receipt=PsoCommandLine.Current.GetString("-pso-build-receipt","");
        if (!string.IsNullOrWhiteSpace(receipt)) File.WriteAllText(receipt,JsonUtility.ToJson(new Receipt {
            result=report.summary.result.ToString(),guid=report.summary.guid.ToString(),bytes=report.summary.totalSize,
            seconds=report.summary.totalTime.TotalSeconds,errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,scenes=Scenes,
            unityVersion=Application.unityVersion,target=target.ToString(),graphicsApi=api.ToString() },true));
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
        string expected=PsoCommandLine.Current.GetString("-pso-external-editor-version","6000.1.0f1");
        if ((expected!="6000.1.0f1" && expected!="6000.5.9f1") || Application.unityVersion!=expected)
            throw new BuildFailedException("Explicit supported external Editor cell required.");
    }
    static BuildTarget Target(out GraphicsDeviceType api)
    {
        string cell=PsoCommandLine.Current.GetString("-pso-external-build-cell","windows-d3d12-v1");
        if (cell=="windows-d3d12-v1") { api=GraphicsDeviceType.Direct3D12; return BuildTarget.StandaloneWindows64; }
        if (cell=="linux-vulkan-v1" && Application.unityVersion=="6000.5.9f1")
        { api=GraphicsDeviceType.Vulkan; return BuildTarget.StandaloneLinux64; }
        throw new BuildFailedException("Unsupported explicit external build cell.");
    }
    [Serializable] sealed class Receipt
    {
        public string result,guid,unityVersion,target,graphicsApi; public ulong bytes; public double seconds;
        public int errors,warnings; public string[] scenes;
    }
}
