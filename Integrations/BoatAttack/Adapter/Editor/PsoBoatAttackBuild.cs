using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BoatAttack.Benchmark;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Yanagisawa.ShaderHitchPipeline;
using Yanagisawa.ShaderHitchPipeline.Editor;

// Copied additively into this pinned host's Assets, so it can use the upstream
// Assembly-CSharp API. It is not part of the distributed runtime package.
public static class PsoBoatAttackBuild
{
    public static readonly string[] Scenes = {
        "Assets/scenes/benchmark/loader.unity",
        "Assets/scenes/Testing/benchmark_island-flythrough.unity",
        "Assets/scenes/Testing/benchmark_island-static.unity"
    };

    public static void Configure()
    {
        ValidateVersion();
        // The same scene sequence used by the upstream BenchmarkWindow, with
        // paths resolved from its actual scene assets rather than stale strings.
        var settings = AssetDatabase.LoadAssetAtPath<BenchmarkConfigData>("Assets/Resources/BenchmarkSettings.asset");
        if (settings == null || settings.benchmarkData.Count != 2)
            throw new BuildFailedException("Unexpected upstream benchmark configuration.");
        for (int i = 0; i < 2; ++i)
        {
            string path = AssetDatabase.GetAssetPath(settings.benchmarkData[i].sceneAsset);
            if (path != Scenes[i + 1]) throw new BuildFailedException("Upstream scene identity changed: " + path);
            settings.benchmarkData[i].scene = path;
        }
        settings.stats = true; // Retain upstream results as well as first-pass telemetry.
        EditorUtility.SetDirty(settings);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Objects/misc/[benchmark].prefab");
        var benchmark = prefab == null ? null : prefab.GetComponent<Benchmark>();
        if (benchmark == null || !benchmark.autoStart || benchmark.simpleRun || benchmark.finish != FinishAction.Exit)
            throw new BuildFailedException("Preserve the upstream automatic complete-suite/Exit prefab.");
        if (settings.benchmarkData[0].runs != 3 || settings.benchmarkData[0].runLength != 500 ||
            settings.benchmarkData[1].runs != 5 || settings.benchmarkData[1].runLength != 25 ||
            settings.benchmarkData.Any(s => !s.warmup))
            throw new BuildFailedException("Preserve all upstream routes and warmup rounds.");
        EditorBuildSettings.scenes = Scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
        Debug.Log("[PSO Boat] Configured original loader, flythrough 3x500 + warmup, static 5x25 + warmup; D3D12/IL2CPP.");
    }

    public static void BuildAddressables()
    {
        ValidateVersion();
        if (AddressableAssetSettingsDefaultObject.Settings == null)
            throw new BuildFailedException("Missing upstream Addressables settings.");
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
        if (!string.IsNullOrEmpty(result.Error)) throw new BuildFailedException(result.Error);
        Debug.Log("[PSO Boat] Original Addressables build succeeded: " + result.OutputPath);
    }

    public static void BuildPlayer()
    {
        ValidateVersion();
        string output = PsoCommandLine.Current.GetString("-pso-native-build-output", "");
        if (string.IsNullOrWhiteSpace(output) || Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
            throw new BuildFailedException("A new Player output directory is required.");
        if (!EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).SequenceEqual(Scenes) ||
            PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.IL2CPP ||
            PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64) ||
            !PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64).SequenceEqual(new[] { GraphicsDeviceType.Direct3D12 }))
            throw new BuildFailedException("Pinned benchmark scene/backend configuration is required.");
        var options = new BuildPlayerOptions { scenes = Scenes, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.Development };
        PsoBuildIdentityCapture.DeclareBuildInputs(options);
        var report = BuildPipeline.BuildPlayer(options);
        string receipt = PsoCommandLine.Current.GetString("-pso-build-receipt", "");
        if (!string.IsNullOrWhiteSpace(receipt)) File.WriteAllText(receipt, JsonUtility.ToJson(new BuildReceipt {
            result = report.summary.result.ToString(), guid = report.summary.guid.ToString(),
            bytes = report.summary.totalSize, seconds = report.summary.totalTime.TotalSeconds,
            errors = report.summary.totalErrors, warnings = report.summary.totalWarnings, scenes = Scenes
        }, true));
        if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Boat Player build failed.");
    }

    public static void ProcessInbox()
    {
        ValidateVersion();
        // Resolve every project shader/material for the offline merge, including
        // Addressables dependencies. This is not a workload shader whitelist.
        var retained = new List<UnityEngine.Object>();
        foreach (string guid in AssetDatabase.FindAssets("t:Shader t:Material"))
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) retained.Add(asset);
        }
        PsoBatch.ProcessInbox();
        GC.KeepAlive(retained);
    }

    static void ValidateVersion()
    {
        if (Application.unityVersion != "6000.1.0f1") throw new BuildFailedException("Requires locked Unity 6000.1.0f1.");
    }
    [Serializable] sealed class BuildReceipt
    {
        public string result, guid;
        public ulong bytes;
        public double seconds;
        public int errors, warnings;
        public string[] scenes;
    }
}
