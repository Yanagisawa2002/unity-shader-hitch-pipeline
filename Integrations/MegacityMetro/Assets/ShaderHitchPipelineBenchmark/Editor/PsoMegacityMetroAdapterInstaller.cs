using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yanagisawa.ShaderHitchPipeline.MegacityMetro.Editor
{
    public static class PsoMegacityMetroAdapterInstaller
    {
        private const int BenchmarkLayer = 29;

        [MenuItem("Tools/Shader Hitch Pipeline/Megacity/Bind Selected Reveal Set")]
        public static void BindSelectedRevealSet()
        {
            Renderer[] renderers = Selection.gameObjects
                .SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
                .Where(item => item != null)
                .Distinct()
                .ToArray();
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Select the upcoming Megacity district roots before binding the adapter.");
            }
            PsoMegacityMetroDeadlineAdapter adapter = BindRendererSet(
                renderers,
                null,
                true);
            Selection.activeGameObject = adapter.gameObject;
        }

        internal static PsoMegacityMetroDeadlineAdapter BindRendererSet(
            Renderer[] renderers,
            Camera requestedCamera,
            bool registerUndo,
            Shader revealShader = null,
            int benchmarkLayer = BenchmarkLayer,
            bool stampRendererLayers = true)
        {
            if (renderers == null || renderers.Length == 0)
                throw new ArgumentException("A non-empty renderer set is required.", nameof(renderers));
            renderers = renderers
                .Where(item => item != null)
                .Distinct()
                .OrderBy(item => HierarchyPath(item.transform), StringComparer.Ordinal)
                .ToArray();
            if (renderers.Any(item => !item.gameObject.scene.IsValid()))
            {
                throw new InvalidOperationException(
                    "The reveal set must come from loaded SubScene authoring objects, " +
                    "not Project-window prefab assets.");
            }

            var selected = new HashSet<Renderer>(renderers);
            Renderer collision = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(item =>
                    item != null &&
                    item.gameObject.layer == benchmarkLayer &&
                    (!stampRendererLayers || !selected.Contains(item)));
            if (collision != null)
            {
                throw new InvalidOperationException(
                    "Layer " + benchmarkLayer + " is already used by an unselected renderer: " +
                    collision.name + ". Reserve a dedicated benchmark layer first.");
            }

            Camera camera = requestedCamera;
            if (camera == null)
                camera = Camera.main;
            if (camera == null)
                camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera == null)
                throw new InvalidOperationException("The open Megacity scene has no camera.");
            Scene hostScene = camera.gameObject.scene;
            if (!hostScene.IsValid() || !hostScene.isLoaded)
                throw new InvalidOperationException("The Megacity camera is not in a loaded scene.");
            if (renderers.Any(item => item.gameObject.scene == hostScene))
            {
                throw new InvalidOperationException(
                    "Keep the benchmark host in the camera scene and select reveal " +
                    "renderers from an opened SubScene, not the camera scene.");
            }

            PsoMegacityMetroDeadlineAdapter adapter =
                UnityEngine.Object.FindFirstObjectByType<PsoMegacityMetroDeadlineAdapter>();
            if (adapter == null)
            {
                var host = new GameObject("Shader Hitch Pipeline - Megacity Benchmark");
                if (registerUndo)
                {
                    Undo.RegisterCreatedObjectUndo(
                        host,
                        "Create Megacity PSO benchmark adapter");
                }
                SceneManager.MoveGameObjectToScene(host, hostScene);
                adapter = host.AddComponent<PsoMegacityMetroDeadlineAdapter>();
            }
            else if (adapter.gameObject.scene != hostScene)
            {
                if (registerUndo && adapter.transform.parent != null)
                    Undo.SetTransformParent(adapter.transform, null, "Move Megacity PSO benchmark host");
                else if (adapter.transform.parent != null)
                    adapter.transform.SetParent(null);
                SceneManager.MoveGameObjectToScene(adapter.gameObject, hostScene);
            }

            Bounds continuityBounds = renderers[0].bounds;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (stampRendererLayers)
                {
                    if (registerUndo)
                        Undo.RecordObject(renderer.gameObject, "Assign Megacity PSO reveal layer");
                    renderer.gameObject.layer = benchmarkLayer;
                    EditorUtility.SetDirty(renderer.gameObject);
                    EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
                }
                if (index > 0)
                    continuityBounds.Encapsulate(renderer.bounds);
            }

            if (registerUndo)
                Undo.RecordObject(adapter, "Bind Megacity PSO reveal set");
            adapter.Configure(
                camera,
                continuityBounds.center,
                renderers.Length,
                benchmarkLayer,
                revealShader);
            EditorUtility.SetDirty(adapter);
            EditorSceneManager.MarkSceneDirty(adapter.gameObject.scene);
            Debug.Log("[ShaderHitchPipeline] Bound " + renderers.Length +
                      " Megacity context renderers; controlled reveal layer=" +
                      benchmarkLayer + ", stampContext=" + stampRendererLayers + ".");
            return adapter;
        }

        private static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }
    }
}
