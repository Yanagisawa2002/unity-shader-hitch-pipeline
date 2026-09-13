using System;
using System.Collections.Generic;
using Unity.NetCode.Hybrid;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Yanagisawa.ShaderHitchPipeline.Editor;
using Yanagisawa.ShaderHitchPipeline.NativeScenes.Editor;

public static class PsoMegacityAcceptanceBuild
{
    public static void Configure()
    {
        ValidateClient();
        PsoNativeHostBuild.ConfigureWindowsD3D12();
        if (QualitySettings.GetQualityLevel() != 2 || QualitySettings.names[2] != "High")
            throw new BuildFailedException("Keep the original High desktop quality.");
        new PsoProjectConfiguration {
            profileId = "megacity-original-single-player-6000.1-d3d12-high",
            targetQualityLevelName = "High", preinteractiveBootstrap = false,
            deferredDeadlineMilliseconds = 1000, deferredExpectedUseProbability = 1
        }.Save();
        // No scene save, new camera, game mode override, quality mutation or
        // reimport-all. The source-level opt-in API owns the declared entry.
        Debug.Log("[PSO Megacity Acceptance] Native Client cell configured; original Menu/Main, High and all source SubScenes retained.");
    }
    public static void BuildPlayer()
    {
        ValidateClient();
        PsoNativeHostBuild.BuildWindowsPlayer();
    }
    public static void ProcessInbox()
    {
        ValidateClient();
        var retained = new List<UnityEngine.Object>();
        foreach (string guid in AssetDatabase.FindAssets("t:Shader t:Material"))
            retained.Add(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
        PsoBatch.ProcessInbox();
        GC.KeepAlive(retained);
    }
    static void ValidateClient()
    {
        if (Application.unityVersion != "6000.1.0f1" || NetCodeClientSettings.instance.ClientTarget != NetCodeClientTarget.Client)
            throw new BuildFailedException("Preserve pinned 6000.1.0f1 / NetCode Client; no server/combined target substitution.");
    }
}
