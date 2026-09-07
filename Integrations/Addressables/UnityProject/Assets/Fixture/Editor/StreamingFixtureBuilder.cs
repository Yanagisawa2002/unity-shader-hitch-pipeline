using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Yanagisawa.ShaderHitchPipeline;

public static class StreamingFixtureBuilder
{
    public static void Build()
    {
        const string generated = "Assets/FixtureGenerated";
        Directory.CreateDirectory(generated);
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Fixture/StreamingFixture.shader");
        if (shader == null) throw new Exception("Fixture shader missing.");
        Add(settings, "SharedShaders", AssetDatabase.GetAssetPath(shader), "stream-shader");
        for (int revision = 1; revision <= 2; revision++)
        {
            string path = generated + "/r" + revision;
            var material = new Material(shader) { name = "Streaming-r" + revision };
            material.SetColor("_Tint", revision == 1 ? Color.blue : Color.red);
            AssetDatabase.CreateAsset(material, path + ".mat");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Streaming-r" + revision;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.GetComponent<Renderer>().sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(cube, path + ".prefab"); UnityEngine.Object.DestroyImmediate(cube);
            Add(settings, "Content-r" + revision, path + ".prefab", "stream-r" + revision);
        }
        AssetDatabase.SaveAssets();
        settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        AddressableAssetSettings.BuildPlayerContent(out var contentResult);
        if (!string.IsNullOrEmpty(contentResult.Error)) throw new Exception(contentResult.Error);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -4);
        new GameObject("Fixture").AddComponent<StreamingFixture>();
        string scenePath = generated + "/Fixture.unity"; EditorSceneManager.SaveScene(scene, scenePath);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
        PlayerSettings.defaultScreenWidth = 480; PlayerSettings.defaultScreenHeight = 320;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        string output = PsoCommandLine.Current.GetString("-stream-output", "Builds/StreamingFixture.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath },
            target = BuildTarget.StandaloneWindows64, locationPathName = Path.GetFullPath(output), options = BuildOptions.None });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Fixture Player build failed: " + report.summary.result);
        Debug.Log("STREAMING_FIXTURE_BUILD_OK");
    }
    private static void Add(AddressableAssetSettings settings, string groupName, string path, string address)
    {
        var group = settings.FindGroup(groupName) ?? settings.CreateGroup(groupName, false, false, false, null,
            typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
        settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group).address = address;
    }
}
