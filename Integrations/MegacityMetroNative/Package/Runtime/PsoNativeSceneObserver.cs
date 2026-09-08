using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline.NativeScenes
{
    /// <summary>Read-only observer of the host's actual SceneSystem loading requests.
    /// It never loads/unloads a scene, creates materials, changes a camera or freezes a world.
    /// Requires -pso-native-host and an independently attested build and plan.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    public partial class PsoNativeSceneObserver : SystemBase
    {
        private EntityQuery scenes;
        private readonly Dictionary<Entity, PsoContentPhaseRequest> loads = new Dictionary<Entity, PsoContentPhaseRequest>();
        private readonly HashSet<Entity> seen = new HashSet<Entity>();
        private readonly List<Entity> retired = new List<Entity>();
        private string revision;
        private bool renderCold;
        private PsoContentPhaseLifecycle lifecycle;
        // One shared lifecycle across client worlds: unloading one world must not cancel another's phase.
        private static PsoContentPhaseLifecycle shared;
        private static int observers;

        private sealed class Sink : IPsoContentPhaseSink
        {
            private static PsoWarmupOrchestrator Pipeline => PsoWarmupOrchestrator.Instance;
            public PsoContentPhaseActivation Activate(string phase)
            {
                var pipeline = Pipeline;
                if (pipeline == null || !pipeline.HasLoadedPlan || pipeline.HasFailed) return PsoContentPhaseActivation.Unavailable;
                if (pipeline.ActivatePhase(phase)) return PsoContentPhaseActivation.Accepted;
                if (pipeline.IsPhaseUnloading(phase)) return PsoContentPhaseActivation.Deferred;
                var status = pipeline.GetPhaseStatus(phase);
                if (status != null && (status.State == PsoPhaseState.Pending || status.State == PsoPhaseState.Running || status.IsComplete))
                    return PsoContentPhaseActivation.Accepted;
                return PsoContentPhaseActivation.Unavailable;
            }
            public void Cancel(string phase)
            {
                var pipeline = Pipeline;
                if (pipeline != null && pipeline.HasLoadedPlan) pipeline.CancelPhase(phase);
            }
            public void Unload(string phase)
            {
                var pipeline = Pipeline;
                if (pipeline != null && pipeline.HasLoadedPlan) pipeline.UnloadPhase(phase);
            }
            public bool IsComplete(string phase)
            {
                var pipeline = Pipeline;
                if (pipeline != null && pipeline.HasFailed) throw new InvalidOperationException(pipeline.Failure);
                return pipeline != null && pipeline.IsPhaseComplete(phase);
            }
        }

        protected override void OnCreate()
        {
            if (!PsoCommandLine.Current.HasFlag("-pso-native-host")) { Enabled = false; return; }
            var identity = PsoUnityBuildIdentity.Capture();
            var issues = PsoCompatibility.ValidateIdentity(identity);
            if (issues.Count != 0)
            {
                Debug.LogError("[PSO Native Scenes] Current build identity unavailable: " + string.Join("; ", issues));
                Enabled = false;
                return;
            }
            revision = identity.contentRevision;
            renderCold = PsoCommandLine.Current.HasFlag(PsoConstants.DisableWarmupArgument);
            scenes = GetEntityQuery(ComponentType.ReadOnly<SceneReference>());
            if (shared == null) shared = new PsoContentPhaseLifecycle(new Sink());
            lifecycle = shared;
            observers++;
        }

        protected override void OnUpdate()
        {
            seen.Clear();
            using (var entities = scenes.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (!EntityManager.HasComponent<RequestSceneLoaded>(entity)) continue;
                    seen.Add(entity);
                    if (!loads.TryGetValue(entity, out var request))
                    {
                        string guid = EntityManager.GetComponentData<SceneReference>(entity).SceneGUID.ToString();
                        string content = World.SequenceNumber + ":" + entity.Index + ":" + entity.Version + ":" + guid;
                        request = lifecycle.Request(content, revision, "scene-" + guid);
                        loads.Add(entity, request);
                    }
                    // Do not ask Unity to resolve GSC shader references before native dependencies exist.
                    // This event may follow the first draw. No first-frame coverage guarantee is made.
                    if (request.Status == PsoContentPhaseStatus.WaitingForDependencies && SceneSystem.IsSceneLoaded(World.Unmanaged, entity))
                    {
                        if (lifecycle.DependenciesReady(request, renderCold) && !renderCold)
                        {
                            var pipeline = PsoWarmupOrchestrator.Instance;
                            if (pipeline != null && pipeline.HasLoadedPlan && !pipeline.FeedbackTraceArmed)
                                pipeline.TryArmFeedbackTrace();
                        }
                        if (request.Status == PsoContentPhaseStatus.Failed)
                            Debug.LogWarning("[PSO Native Scenes] " + request.Phase + ": " + request.Failure);
                    }
                }
            }
            retired.Clear();
            foreach (var load in loads)
            {
                if (seen.Contains(load.Key)) continue;
                if (load.Value.Status == PsoContentPhaseStatus.WaitingForDependencies) lifecycle.Cancel(load.Value);
                else lifecycle.Unload(load.Value);
                retired.Add(load.Key);
            }
            foreach (var entity in retired) loads.Remove(entity);
            lifecycle.Refresh();
        }

        protected override void OnDestroy()
        {
            if (lifecycle == null) return;
            foreach (var request in loads.Values) lifecycle.Unload(request);
            loads.Clear();
            if (--observers == 0) { shared.Dispose(); shared = null; }
            lifecycle = null;
        }
    }
}
