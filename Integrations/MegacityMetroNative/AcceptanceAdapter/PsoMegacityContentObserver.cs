using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.MegacityMetro.Gameplay;
using Unity.MegacityMetro.Streaming;
using Unity.MegacityMetro.Traffic;
using Unity.Rendering;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;
using Yanagisawa.ShaderHitchPipeline;

// Host-only read observer. It never creates/changes entities, load requests,
// simulation groups, transforms or render components. These public SceneTag
// queries count the actual payload of each resolved section, not six labels.
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(SceneSystemGroup))]
public partial class PsoMegacityContentObserver : SystemBase
{
    [Serializable] public sealed class Section
    {
        public string guid;
        public int entity, version, index, fileBytes, objectReferences, payloadEntities, renderEntities;
        public bool requested, loaded;
    }
    [Serializable] public sealed class Scene
    {
        public string guid;
        public int entity, version;
        public bool requested, loaded;
        public Section[] sections;
    }
    [Serializable] public sealed class Sample
    {
        public string kind;
        public int entity, version, roadIndex;
        public Vector3 position;
        public Quaternion rotation;
        public float splinePosition, blimpRotation;
    }
    [Serializable] public sealed class Snapshot
    {
        public string world;
        public ulong worldSequence;
        public int frame, singlePlayers, vehicles, blimps, renderEntities, requestedSections, loadedSections;
        public double realtimeSeconds, simulationSeconds;
        public bool gameLoadInfoPresent, allExpectedScenesLoaded;
        public Scene[] scenes;
        public Sample[] samples;
    }
    public static readonly string[] ExpectedGuids = {
        "d15a274585a786440ad97fbd9f40d43a", // Blimps
        "3326447b997a6814bab701262e9f6f38", // Common
        "ed1a49ee1f7b28b499c8cece71ee2353", // Level
        "f98b38b047e44c14ab1e89f0c3d96b12", // MegacityMetroLevelBounds
        "49a72a7c2a2c8044abfaf7829f316369", // Player_Subscene
        "46dffb08de5f4cf498cabf1a709740e0"  // Traffic
    };
    EntityQuery scenes, payload, sectionRenders, renders, player, traffic, blimps, loadInfo;
    readonly Dictionary<string, List<Entity>> tracked = new Dictionary<string, List<Entity>>();
    double nextSample;
    bool previouslyReady;

    protected override void OnCreate()
    {
        if (!PsoCommandLine.Current.HasFlag("-pso-external-capture")) { Enabled = false; return; }
        scenes = GetEntityQuery(ComponentType.ReadOnly<SceneReference>());
        payload = GetEntityQuery(new EntityQueryDesc {
            All = new[] { ComponentType.ReadOnly<SceneTag>() },
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab });
        sectionRenders = GetEntityQuery(ComponentType.ReadOnly<SceneTag>(), ComponentType.ReadOnly<MaterialMeshInfo>());
        renders = GetEntityQuery(ComponentType.ReadOnly<MaterialMeshInfo>());
        player = GetEntityQuery(ComponentType.ReadOnly<SinglePlayer>(), ComponentType.ReadOnly<LocalTransform>());
        traffic = GetEntityQuery(ComponentType.ReadOnly<VehiclePathing>(), ComponentType.ReadOnly<LocalTransform>());
        blimps = GetEntityQuery(ComponentType.ReadOnly<BlimpComponent>(), ComponentType.ReadOnly<LocalTransform>());
        loadInfo = GetEntityQuery(ComponentType.ReadOnly<GameLoadInfo>());
    }

    protected override void OnUpdate()
    {
        var capture = PsoMegacityAcceptanceCapture.Instance;
        if (capture == null || UnityEngine.Time.realtimeSinceStartupAsDouble < nextSample) return;
        nextSample = UnityEngine.Time.realtimeSinceStartupAsDouble + (previouslyReady ? 1.0 : 0.25);
        var list = new List<Scene>();
        var ready = new HashSet<string>(StringComparer.Ordinal);
        using (var roots = scenes.ToEntityArray(Allocator.Temp))
        foreach (var root in roots)
        {
            string guid = EntityManager.GetComponentData<SceneReference>(root).SceneGUID.ToString();
            var value = new Scene { guid = guid, entity = root.Index, version = root.Version,
                requested = EntityManager.HasComponent<RequestSceneLoaded>(root),
                loaded = SceneSystem.IsSceneLoaded(World.Unmanaged, root) };
            var sections = new List<Section>();
            if (EntityManager.HasBuffer<ResolvedSectionEntity>(root))
            {
                var resolved = EntityManager.GetBuffer<ResolvedSectionEntity>(root, true);
                foreach (var item in resolved)
                {
                    var entity = item.SectionEntity;
                    if (!EntityManager.HasComponent<SceneSectionData>(entity)) continue;
                    var data = EntityManager.GetComponentData<SceneSectionData>(entity);
                    var tag = new SceneTag { SceneEntity = entity };
                    payload.SetSharedComponentFilter(tag);
                    sectionRenders.SetSharedComponentFilter(tag);
                    sections.Add(new Section { guid = data.SceneGUID.ToString(), entity = entity.Index,
                        version = entity.Version, index = data.SubSectionIndex, fileBytes = data.FileSize,
                        objectReferences = data.ObjectReferenceCount,
                        requested = EntityManager.HasComponent<RequestSceneLoaded>(entity),
                        loaded = SceneSystem.IsSectionLoaded(World.Unmanaged, entity),
                        payloadEntities = payload.CalculateEntityCount(), renderEntities = sectionRenders.CalculateEntityCount() });
                }
            }
            value.sections = sections.ToArray();
            // Every resolved section must actually be requested and loaded, and
            // a positive payload must exist for this scene. Metadata alone fails.
            bool complete = value.requested && value.loaded && sections.Count > 0;
            int count = 0;
            foreach (var section in sections) { complete &= section.requested && section.loaded; count += section.payloadEntities; }
            if (complete && count > 0) ready.Add(guid);
            list.Add(value);
        }
        payload.ResetFilter(); sectionRenders.ResetFilter();
        bool allReady = true;
        foreach (string expected in ExpectedGuids) allReady &= ready.Contains(expected);
        var snapshot = new Snapshot { world = World.Name, worldSequence = World.SequenceNumber,
            frame = UnityEngine.Time.frameCount, realtimeSeconds = UnityEngine.Time.realtimeSinceStartupAsDouble,
            simulationSeconds = World.Time.ElapsedTime, scenes = list.ToArray(), allExpectedScenesLoaded = allReady,
            singlePlayers = player.CalculateEntityCount(), vehicles = traffic.CalculateEntityCount(),
            blimps = blimps.CalculateEntityCount(), renderEntities = renders.CalculateEntityCount() };
        if (loadInfo.CalculateEntityCount() == 1)
        {
            var info = loadInfo.GetSingleton<GameLoadInfo>();
            snapshot.gameLoadInfoPresent = true;
            snapshot.requestedSections = info.TotalSceneSections;
            snapshot.loadedSections = info.LoadedSceneSections;
        }
        var samples = new List<Sample>();
        SampleEntities(player, "single-player", 2, samples);
        SampleEntities(traffic, "traffic", 8, samples);
        SampleEntities(blimps, "blimp", 4, samples);
        snapshot.samples = samples.ToArray();
        previouslyReady = allReady;
        capture.Observe(snapshot);
    }

    void SampleEntities(EntityQuery query, string kind, int limit, List<Sample> output)
    {
        if (!tracked.TryGetValue(kind, out var selected)) tracked.Add(kind, selected = new List<Entity>());
        // Stable entity identity across observations, replacing only despawned
        // entities. Sampling limits evidence overhead, never workload population.
        selected.RemoveAll(e => !EntityManager.Exists(e) || !EntityManager.HasComponent<LocalTransform>(e) || !HasOriginalKind(e, kind));
        if (selected.Count < limit)
        {
            using var candidates = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in candidates)
            {
                if (!selected.Contains(entity)) selected.Add(entity);
                if (selected.Count >= limit) break;
            }
        }
        foreach (var entity in selected)
        {
            var transform = EntityManager.GetComponentData<LocalTransform>(entity);
            var value = new Sample { kind = kind, entity = entity.Index, version = entity.Version,
                position = transform.Position, rotation = transform.Rotation };
            if (EntityManager.HasComponent<VehiclePathing>(entity))
            {
                var path = EntityManager.GetComponentData<VehiclePathing>(entity);
                value.splinePosition = path.SplinePos; value.roadIndex = path.RoadIndex;
            }
            if (EntityManager.HasComponent<BlimpComponent>(entity))
                value.blimpRotation = EntityManager.GetComponentData<BlimpComponent>(entity).Rotation;
            output.Add(value);
        }
    }

    bool HasOriginalKind(Entity entity, string kind)
    {
        return kind == "traffic" ? EntityManager.HasComponent<VehiclePathing>(entity) :
            kind == "blimp" ? EntityManager.HasComponent<BlimpComponent>(entity) :
            kind == "single-player" && EntityManager.HasComponent<SinglePlayer>(entity);
    }
}
