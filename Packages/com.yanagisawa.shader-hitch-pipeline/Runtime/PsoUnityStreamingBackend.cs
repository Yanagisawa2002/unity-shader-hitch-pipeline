using System;
using System.Collections.Generic;
using System.IO;
using Unity.Jobs;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>Immutable artifact expectations from the attested plan, not from a partially resolved native load.</summary>
    public sealed class PsoStreamingCollectionAsset
    {
        public string Path { get; }
        public string Sha256 { get; }
        public int ExpectedStateCount { get; }
        public PsoStreamingCollectionAsset(string path, string sha256, int expectedStateCount)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A collection path is required.");
            if (sha256 == null || sha256.Length != 64) throw new ArgumentException("A SHA256 artifact digest is required.");
            foreach (char c in sha256) if (!Uri.IsHexDigit(c)) throw new ArgumentException("Invalid SHA256 digest.");
            if (expectedStateCount < 1) throw new ArgumentOutOfRangeException(nameof(expectedStateCount));
            Path = System.IO.Path.GetFullPath(path); Sha256 = sha256; ExpectedStateCount = expectedStateCount;
        }
    }

    /// <summary>Exact native state interning and subset submission. Construct/use on the Unity main thread.
    /// IDs are session-local; they are deliberately not trace IDs or persisted compatibility evidence.</summary>
    public sealed class PsoUnityStreamingBackend : IPsoStreamingBackend
    {
        private sealed class Record
        {
            internal GraphicsStateCollection.ShaderVariant variant;
            internal GraphicsStateCollection.GraphicsState state;
            internal PsoStreamingState descriptor;
            internal int references;
        }
        private readonly List<Record> records = new List<Record>();
        private long sequence;
        private readonly string session = Guid.NewGuid().ToString("N");
        public int InternedStateCount => records.Count;

        /// <summary>The caller has already loaded ALL material/shader dependencies and validated the content revision.
        /// Each path is a complete collection file, opened only now, so Unity resolves the loaded bundle shaders.
        /// Takes ownership of releaseAssets even on failure.</summary>
        public PsoStreamOwner RegisterLoaded(PsoStreamingCoordinator coordinator, string contentId, string contentRevision,
            string compatibilityIdentity, IReadOnlyDictionary<string, PsoStreamingCollectionAsset> collections, Action releaseAssets)
        {
            if (releaseAssets == null) throw new ArgumentNullException(nameof(releaseAssets));
            var retained = new HashSet<Record>();
            var asset = new PsoRetainedAsset(() =>
            {
                foreach (var record in retained) if (--record.references == 0) records.Remove(record);
                releaseAssets();
            });
            try
            {
                var phases = new Dictionary<string, IReadOnlyList<PsoStreamingState>>(StringComparer.Ordinal);
                foreach (var phase in collections)
                {
                    if (phase.Value == null || !string.Equals(PsoFileUtility.ComputeSha256(phase.Value.Path), phase.Value.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Streaming collection artifact integrity mismatch: " + phase.Key);
                    var source = new GraphicsStateCollection();
                    try
                    {
                        if (!source.LoadFromFile(phase.Value.Path)) throw new IOException("Cannot load streaming collection: " + phase.Value.Path);
                        if (source.runtimePlatform != Application.platform || source.graphicsDeviceType != SystemInfo.graphicsDeviceType)
                            throw new InvalidDataException("Streaming collection platform/API mismatch.");
                        if (source.totalGraphicsStateCount != phase.Value.ExpectedStateCount)
                            throw new InvalidDataException("Streaming collection state count differs from attested plan; verify loaded shader dependencies or retrace.");
                        var variants = new List<GraphicsStateCollection.ShaderVariant>(); source.GetVariants(variants);
                        var list = new List<PsoStreamingState>();
                        foreach (var variant in variants)
                        {
                            if (variant.shader == null) throw new InvalidDataException("Collection shader is unresolved; load content dependencies before registration.");
                            var states = new List<GraphicsStateCollection.GraphicsState>(); source.GetGraphicsStatesForVariant(variant, states);
                            foreach (var state in states)
                            {
                                var record = Intern(variant, state);
                                if (retained.Add(record)) record.references++;
                                list.Add(record.descriptor);
                            }
                        }
                        if (list.Count != source.totalGraphicsStateCount)
                            throw new InvalidDataException("Collection contains unresolved or omitted graphics states.");
                        phases.Add(phase.Key, list);
                    }
                    finally { UnityEngine.Object.Destroy(source); }
                }
                return coordinator.RegisterLoaded(contentId, contentRevision, compatibilityIdentity, phases, asset);
            }
            finally { asset.Dispose(); }
        }

        private static bool Add(GraphicsStateCollection collection, GraphicsStateCollection.ShaderVariant variant,
            GraphicsStateCollection.GraphicsState state)
        {
            collection.AddVariant(variant.shader, variant.passId, variant.keywords);
            return collection.AddGraphicsStateForVariant(variant.shader, variant.passId, variant.keywords, state);
        }
        private Record Intern(GraphicsStateCollection.ShaderVariant variant, GraphicsStateCollection.GraphicsState state)
        {
            // Ask Unity to compare complete states (including nested arrays), rather than hashing incomplete
            // managed fields or shader names. A bounded content import may cost O(N^2); no timing claim is made.
            foreach (var existing in records)
            {
                if (existing.variant.shader != variant.shader || !existing.variant.passId.Equals(variant.passId)) continue;
                var probe = new GraphicsStateCollection();
                try
                {
                    if (!Add(probe, variant, state)) throw new InvalidDataException("Native state insertion failed.");
                    Add(probe, existing.variant, existing.state);
                    if (probe.totalGraphicsStateCount == 1) return existing;
                }
                finally { UnityEngine.Object.Destroy(probe); }
            }
            var record = new Record { variant = variant, state = state };
            record.descriptor = new PsoStreamingState(session + ":" + (++sequence), record);
            records.Add(record); return record;
        }

        public IPsoWarmupBatch Submit(IReadOnlyList<PsoStreamingState> states)
        {
            var collection = new GraphicsStateCollection
            {
                runtimePlatform = Application.platform,
                graphicsDeviceType = SystemInfo.graphicsDeviceType,
                qualityLevelName = PsoUnityEnvironment.CurrentQualityName()
            };
            try
            {
                foreach (var descriptor in states)
                {
                    var record = descriptor.Payload as Record;
                    if (record == null || record.references == 0 || !records.Contains(record)) throw new InvalidOperationException("Stale or foreign native state.");
                    Add(collection, record.variant, record.state);
                }
                if (collection.totalGraphicsStateCount == 0) throw new InvalidOperationException("No native states to warm.");
                // Allocate the fence object before submitting, so no allocation can lose an already submitted job.
                var batch = new Batch(collection);
                batch.Schedule();
                return batch;
            }
            catch { UnityEngine.Object.Destroy(collection); throw; }
        }
        private sealed class Batch : IPsoWarmupBatch, IPsoStreamingBatchResult
        {
            private GraphicsStateCollection collection;
            private JobHandle job;
            private bool fenced;
            internal Batch(GraphicsStateCollection collection) { this.collection = collection; }
            internal void Schedule()
            {
#if UNITY_6000_5_OR_NEWER
                // WarmUp resolves against shaders that have already loaded and deduplicated in Unity.
                job = collection.WarmUp(default(JobHandle), false);
#else
                job = collection.WarmUp(default(JobHandle));
#endif
            }
            public bool IsCompleted => fenced || job.IsCompleted;
            public string Failure { get; private set; }
            public void Complete()
            {
                if (!fenced) { job.Complete(); fenced = true; }
                if (!collection.isWarmedUp) Failure = "Native job fenced but collection did not finish warmup.";
            }
            public void Dispose()
            {
                if (collection == null) return;
                Complete(); UnityEngine.Object.Destroy(collection); collection = null;
            }
        }
    }
}
